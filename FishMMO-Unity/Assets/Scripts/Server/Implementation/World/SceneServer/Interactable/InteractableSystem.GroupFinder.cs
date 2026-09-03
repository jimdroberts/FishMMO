using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
// The DB-side SceneType and SceneStatus enums (FishMMO.Database.Data.Enums)
// collide with the game-side ones (FishMMO.Shared) that this file also uses.
// The unqualified name resolves to the shared enum here; the DB enum is used
// explicitly by its full name at the DB boundary.
using SceneType = FishMMO.Shared.SceneType;
using SceneStatus = FishMMO.Shared.SceneStatus;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Group finder: queues players who want a group for a dungeon, forms groups from the queue,
	/// fills empty slots in runs already open, and moves matched players into their instance.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where the state lives.</b> The queue is a database table shared by every scene server on
	/// the world server, because the people who will end up in one group are connected to
	/// different scene servers. Each scene server keeps only a list of <em>its own</em> queued
	/// characters — the connections it can actually move — and runs one pump against the shared
	/// table on their behalf: heartbeat, read back their rows, act on the ones that were matched,
	/// try to fill and form groups for the ones still waiting.
	/// </para>
	/// <para>
	/// <b>There is no matchmaker process.</b> Every scene server with waiters attempts to form a
	/// group for them on every pump, and the database makes that safe: forming a group is one
	/// transaction that locks the waiters it takes, so two servers trying at once produce one
	/// group and one no-op. A server that formed a group does not have to reach the other
	/// members' servers; their own pumps see the matched rows and move them.
	/// </para>
	/// <para>
	/// <b>Two ways in.</b> A waiter is either placed in a fresh group once enough have gathered,
	/// or — checked first, because it is instant — added to a run somebody already opened
	/// publicly that has room. The second is the late-join path: a party of three who opened
	/// their dungeon to others are filled by the next two people who press Find Group, which is
	/// how a partial group uses the finder. A party never queues as a party; its leader opens
	/// the dungeon publicly instead, and the finder fills it.
	/// </para>
	/// <para>
	/// <b>Being matched joins a party.</b> Exactly as the entrance's Join does, and for the same
	/// reasons: the instance's leadership, kick authority and identity are all the owning party's.
	/// So a character in a party with other people cannot queue — they would have to leave it —
	/// and one alone in a party of their own is released from it when they queue.
	/// </para>
	/// <para>
	/// <b>Waiting is done at the door.</b> A queued character has to stay within a short leash of
	/// the entrance they queued at, with the finder panel open. Walking off removes them from the
	/// queue with the reason; closing the panel leaves it. A matched character who has stepped
	/// outside the leash is not moved until they step back, and is dropped from the group if they
	/// stay away past the transfer grace. This is what makes being moved into the dungeon never a
	/// surprise to somebody who has wandered off to do something else.
	/// </para>
	/// </remarks>
	public partial class InteractableSystem
	{
		/// <summary>Ingress guard operation code for joining the queue.</summary>
		private const byte GroupFinderQueueOperation = 12;

		/// <summary>Ingress guard operation code for leaving the queue.</summary>
		private const byte GroupFinderLeaveOperation = 13;

		/// <summary>Minimum milliseconds between queue requests from one connection.</summary>
		private const int GroupFinderQueueDebounceMilliseconds = 2000;

		/// <summary>Minimum milliseconds between leave requests from one connection.</summary>
		private const int GroupFinderLeaveDebounceMilliseconds = 1000;

		/// <summary>
		/// Seconds between group finder pumps.
		/// </summary>
		/// <remarks>
		/// Each pump is a handful of small queries for a server that has waiters and nothing at
		/// all for one that does not. Two seconds is fast enough that a group forming feels
		/// immediate and slow enough that a shard's worth of scene servers is not a standing load.
		/// </remarks>
		[Header("Group Finder")]
		[Tooltip("Seconds between group finder pumps. Each pump is a few small queries per scene server with waiters.")]
		[SerializeField] private float groupFinderPumpIntervalSeconds = 2.0f;

		/// <summary>
		/// Seconds without a heartbeat before a queue row is ignored by matching.
		/// </summary>
		/// <remarks>
		/// Several pump intervals, so one slow database round trip does not drop a live waiter
		/// out of the count, and short enough that a scene server which died with people queued
		/// does not keep phantom waiters in everybody else's counts for long.
		/// </remarks>
		[Tooltip("Seconds without a heartbeat before a queue row is ignored by matching. Rows twice this old are deleted.")]
		[SerializeField] private float groupFinderStalePulseSeconds = 30.0f;

		/// <summary>
		/// Seconds a matched character may stay untransferable — in combat, dead — before the
		/// group goes on without them.
		/// </summary>
		[Tooltip("Seconds a matched player may stay in combat or dead before their group leaves without them.")]
		[SerializeField] private float groupFinderTransferGraceSeconds = 60.0f;

		/// <summary>
		/// Seconds before a waiter whose late-join was refused is offered another run.
		/// </summary>
		[Tooltip("Seconds before a waiter whose late-join into an open run was refused is tried against open runs again.")]
		[SerializeField] private float groupFinderBackfillRetrySeconds = 10.0f;

		/// <summary>
		/// Seconds between sweeps of rows whose heartbeat stopped.
		/// </summary>
		[Tooltip("Seconds between sweeps that delete queue rows whose heartbeat has stopped.")]
		[SerializeField] private float groupFinderStaleSweepIntervalSeconds = 30.0f;

		/// <summary>
		/// How far, in metres, a waiter may stand from the entrance they queued at before they
		/// are dropped from the queue.
		/// </summary>
		/// <remarks>
		/// Wider than the interaction range, which is a touch distance, so a player pacing about
		/// or making room for others at the door is not thrown out of line by it — but a leash,
		/// so nobody is queued from across the zone and moved into a dungeon they have walked away
		/// from. Measured from the entrance's own transform.
		/// </remarks>
		[Tooltip("Metres a waiter may stand from the entrance before they are dropped from the queue.")]
		[SerializeField] private float groupFinderLeashMeters = 8.0f;

		/// <summary>
		/// One character this scene server has in the queue. Main thread only.
		/// </summary>
		private sealed class GroupFinderEntry
		{
			public NetworkConnection Connection;
			public IPlayerCharacter Character;
			public long CharacterID;
			public long WorldServerID;
			public string SceneName;
			public int DungeonTemplateID;
			public int Difficulty;
			public int Capacity;
			public int GroupSize;
			public WorldSceneDetails SceneDetails;
			public AchievementTemplate AchievementTemplate;

			/// <summary>The entrance they queued at; the leash is measured from it.</summary>
			public IInteractable Entrance;

			/// <summary>What this server last told the client.</summary>
			public GroupFinderState State;

			/// <summary>Last waiting count sent, so the pump only speaks when the number moves.</summary>
			public int LastSentWaitingCount = -1;

			/// <summary>When this server first saw the row matched, for the transfer grace.</summary>
			public DateTime MatchedAtUtc;

			/// <summary>Party the row was matched into.</summary>
			public long MatchedPartyID;

			/// <summary>Instance the row was matched into.</summary>
			public long MatchedInstanceID;

			/// <summary>Earliest time the pump may try to late-join this waiter into an open run.</summary>
			public DateTime NextBackfillAttemptUtc;
		}

		/// <summary>
		/// A waiter as the async half of the pump sees it: plain values, and the connection the
		/// party system needs for a late-join.
		/// </summary>
		private struct GroupFinderPumpItem
		{
			public NetworkConnection Connection;
			public long CharacterID;
			public long WorldServerID;
			public string SceneName;
			public int Difficulty;
			public int Capacity;
			public int GroupSize;
			public float HealthPCT;
			public bool BackfillDue;
		}

		/// <summary>This server's queued characters, by character ID. Main thread only.</summary>
		private readonly Dictionary<long, GroupFinderEntry> groupFinderEntries = new Dictionary<long, GroupFinderEntry>();

		/// <summary>
		/// 1 while a pump's async half is running, 0 otherwise. One at a time.
		/// </summary>
		/// <remarks>
		/// An int for <see cref="Interlocked"/>, and cleared by the worker itself rather than via
		/// the main-thread queue: a queue that refused the clearing action would have left the flag
		/// set forever and silently stopped every future pump.
		/// </remarks>
		private int groupFinderPumpInFlight;

		/// <summary>Next time the stale-row sweep runs. Runs whether or not anybody is queued here.</summary>
		private DateTime nextGroupFinderStaleSweepUtc;

		/// <summary>
		/// The moment before which a heartbeat counts as stopped.
		/// </summary>
		private DateTime GroupFinderStaleBefore => DateTime.UtcNow.AddSeconds(-groupFinderStalePulseSeconds);

		/// <summary>
		/// Registers the group finder's requests and pump. Called from InitializeOnce.
		/// </summary>
		private void InitializeGroupFinder()
		{
			groupFinderEntries.Clear();
			Interlocked.Exchange(ref groupFinderPumpInFlight, 0);
			nextGroupFinderStaleSweepUtc = DateTime.UtcNow;

			groupFinderPumpIntervalSeconds = Mathf.Max(0.5f, groupFinderPumpIntervalSeconds);
			groupFinderStalePulseSeconds = Mathf.Max(groupFinderPumpIntervalSeconds * 3.0f, groupFinderStalePulseSeconds);
			groupFinderTransferGraceSeconds = Mathf.Max(groupFinderPumpIntervalSeconds, groupFinderTransferGraceSeconds);
			groupFinderBackfillRetrySeconds = Mathf.Max(groupFinderPumpIntervalSeconds, groupFinderBackfillRetrySeconds);
			groupFinderStaleSweepIntervalSeconds = Mathf.Max(5.0f, groupFinderStaleSweepIntervalSeconds);
			groupFinderLeashMeters = Mathf.Max(1.0f, groupFinderLeashMeters);

			Server.NetworkWrapper.RegisterBroadcast<GroupFinderQueueBroadcast>(OnServerGroupFinderQueueBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GroupFinderLeaveBroadcast>(OnServerGroupFinderLeaveBroadcastReceived, true);

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(groupFinderPumpIntervalSeconds, OnGroupFinderPump);
			}
			else
			{
				Log.Warning("InteractableSystem", "Group finder: the server is not a periodic update system; nobody will be matched.");
			}
		}

		/// <summary>
		/// Unregisters the group finder. Called from OnDeinitialize.
		/// </summary>
		/// <remarks>
		/// The rows of characters queued here are left to the stale sweep rather than deleted:
		/// deinitialisation is the shutdown path, the database may already be going away, and a
		/// heartbeat that stops is exactly the signal the sweep exists to act on.
		/// </remarks>
		private void DeinitializeGroupFinder()
		{
			Server.NetworkWrapper.UnregisterBroadcast<GroupFinderQueueBroadcast>(OnServerGroupFinderQueueBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GroupFinderLeaveBroadcast>(OnServerGroupFinderLeaveBroadcastReceived);

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnGroupFinderPump);
			}

			groupFinderEntries.Clear();
			Interlocked.Exchange(ref groupFinderPumpInFlight, 0);
		}

		// ──────────────────────────────────────────────────────────────────
		//  Requests
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Handles a request to be found a group for one dungeon at one difficulty.
		/// </summary>
		/// <remarks>
		/// Validated like the finder's other requests — the player must be standing at the
		/// entrance — but not gated on character state, because joining a queue is not a move.
		/// The move comes later, from the pump, and is gated then.
		/// </remarks>
		public void OnServerGroupFinderQueueBroadcastReceived(NetworkConnection conn, GroupFinderQueueBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, GroupFinderQueueOperation, GroupFinderQueueDebounceMilliseconds, out long guardKey))
			{
				SendGroupFinderRefusal(conn, GroupFinderRefusalReason.OnCooldown);
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				if (!TryResolveDungeonEntrance(conn, msg.InteractableID, out DungeonRequestContext context))
				{
					SendGroupFinderRefusal(conn, GroupFinderRefusalReason.NoEntrance);
					return;
				}

				if (!TryResolveDifficulty(context.DungeonTemplateID, msg.Difficulty, out DungeonDifficultyDefinition difficulty))
				{
					SendGroupFinderRefusal(conn, GroupFinderRefusalReason.UnknownDifficulty);
					return;
				}

				if (context.SceneDetails.RespawnPositions == null || context.SceneDetails.RespawnPositions.Count < 1)
				{
					Log.Debug("InteractableSystem", $"Group finder refused for {context.DungeonName}: the scene has no respawn points.");
					SendGroupFinderRefusal(conn, GroupFinderRefusalReason.NotAvailable);
					return;
				}

				int capacity = difficulty.ResolveCapacity(context.SceneDetails.MaxClients);
				int groupSize = GroupFinderRules.ResolveGroupSize(difficulty, capacity);

				/* Already matched here: the group is real and the transfer is coming. A second
				 * press is answered with the truth rather than treated as a new request. */
				if (groupFinderEntries.TryGetValue(context.CharacterID, out GroupFinderEntry existing) &&
					existing.State == GroupFinderState.Matched)
				{
					SendGroupFinderStatus(conn, existing, GroupFinderState.Matched, GroupFinderRefusalReason.None, existing.LastSentWaitingCount);
					return;
				}

				/* Whether they share a party with anybody needs the roster, which is a database
				 * read; the async half decides that. Everything decidable here is decided here. */
				GroupFinderRefusalReason refusal = GroupFinderRules.ResolveQueueRefusal(groupSize, context.Character.IsInInstance(), inPartyWithOthers: false);
				if (refusal != GroupFinderRefusalReason.None)
				{
					SendGroupFinderRefusal(conn, refusal);
					return;
				}

				DungeonRequestContext captured = context;
				int requestedDifficulty = msg.Difficulty;

				if (TryEnqueueAsyncWork(
					() => ProcessGroupFinderQueueAsync(conn, captured, requestedDifficulty, capacity, groupSize, guardKey),
					conn,
					context.CharacterID))
				{
					asyncOwnsGuard = true;
				}
				else
				{
					SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError);
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
		/// Clears the character's own party if they are alone in it, checks they hold no
		/// instance, and puts them in the queue.
		/// </summary>
		private async Task ProcessGroupFinderQueueAsync(
			NetworkConnection conn,
			DungeonRequestContext context,
			int difficultyIndex,
			int capacity,
			int groupSize,
			long guardKey)
		{
			long characterID = context.CharacterID;
			long worldServerID = context.WorldServerID;
			string dungeonName = context.DungeonName;

			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IGroupFinderQueueService>(out var queueService) ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError));
					return;
				}

				/* A party with anybody else in it refuses the request; a party of one is released.
				 * The same rule, and the same code, as joining another group's run from the list:
				 * being matched will put this character in a party the finder builds, and that
				 * cannot silently take them out of a group they are already in. */
				switch (await TryReleaseOwnPartyAsync(conn, characterID, context.PartyID, "queuing in the group finder"))
				{
					case OwnPartyReleaseOutcome.Released:
						break;
					case OwnPartyReleaseOutcome.WithOthers:
					case OwnPartyReleaseOutcome.RemovalRefused:
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.InParty));
						return;
					default:
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError));
						return;
				}

				/* An instance they already hold would make the group's own instance insert refuse
				 * the whole group at match time, blocking everybody else in it for as long as this
				 * character sat at the front of the line. Refused now, while the answer can still be
				 * explained to the one person it concerns. */
				var heldResult = await sceneService.FetchCharacterInstancesAsync(
					new List<long>(1) { characterID }, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group, worldServerID);
				if (!heldResult.IsSuccess)
				{
					TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError));
					return;
				}
				foreach (SceneData held in heldResult.Data)
				{
					if (IsUsableInstance(held, worldServerID))
					{
						TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.HoldsInstance));
						return;
					}
				}

				DatabaseResult<long> enqueueResult = await queueService.EnqueueAsync(
					worldServerID, characterID, dungeonName, difficultyIndex, GroupFinderStaleBefore);
				if (!enqueueResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Group finder could not queue character {characterID} for '{dungeonName}': {enqueueResult.ErrorCode} - {enqueueResult.ErrorMessage}");
					TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError));
					return;
				}

				if (enqueueResult.Data <= 0)
				{
					/* A live matched row. Two ways here. A matcher on another server took this
					 * character's existing entry in the instant between the click and the upsert —
					 * the upsert waited on its row lock, re-evaluated, and declined to re-point a
					 * matched row — in which case this server has an entry and its next pump moves
					 * them; the reply is that entry's state, and the pump corrects it within an
					 * interval. Or the row belongs to a previous scene server that matched them and
					 * lost them within the stale window, and re-points itself once it passes. */
					TryEnqueueMainThread(() =>
					{
						if (groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry existing))
						{
							SendGroupFinderStatus(conn, existing, existing.State, GroupFinderRefusalReason.None, Math.Max(0, existing.LastSentWaitingCount));
							return;
						}

						Log.Warning("InteractableSystem",
							$"Group finder: character {characterID} has a live matched queue row this server did not create; refusing to re-queue them until it goes stale.");
						SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError);
					});
					return;
				}

				var countResult = await queueService.CountWaitingAsync(worldServerID, dungeonName, difficultyIndex, GroupFinderStaleBefore);
				int waiting = countResult.IsSuccess ? Math.Max(1, countResult.Data) : 1;

				DungeonRequestContext captured = context;
				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null)
					{
						// Gone between the insert and now. The disconnect hook, or the sweep, removes the row.
						return;
					}

					GroupFinderEntry entry = new GroupFinderEntry
					{
						Connection = conn,
						Character = captured.Character,
						CharacterID = characterID,
						WorldServerID = worldServerID,
						SceneName = dungeonName,
						DungeonTemplateID = captured.DungeonTemplateID,
						Difficulty = difficultyIndex,
						Capacity = capacity,
						GroupSize = groupSize,
						SceneDetails = captured.SceneDetails,
						AchievementTemplate = captured.AchievementTemplate,
						Entrance = captured.Entrance,
						State = GroupFinderState.Waiting,
						NextBackfillAttemptUtc = DateTime.UtcNow,
					};
					groupFinderEntries[characterID] = entry;

					SendGroupFinderStatus(conn, entry, GroupFinderState.Waiting, GroupFinderRefusalReason.None, waiting);
					Log.Debug("InteractableSystem", $"Group finder: character {characterID} queued for '{dungeonName}' at difficulty {difficultyIndex} ({waiting}/{groupSize}).");
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error queuing character {characterID} in the group finder: {ex}");
				TryEnqueueMainThread(() => SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError));
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles a request to leave the queue.
		/// </summary>
		/// <remarks>
		/// Refused once matched. The group has formed and this character is already in its party;
		/// backing out now would leave a party with a member who never arrives. The client hides
		/// its Leave control at that point, so reaching this is a race with the pump, not a click.
		/// </remarks>
		public void OnServerGroupFinderLeaveBroadcastReceived(NetworkConnection conn, GroupFinderLeaveBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, GroupFinderLeaveOperation, GroupFinderLeaveDebounceMilliseconds, out long guardKey))
			{
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character == null)
				{
					return;
				}

				long characterID = character.ID;

				if (groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry entry) &&
					entry.State == GroupFinderState.Matched)
				{
					SendGroupFinderStatus(conn, entry, GroupFinderState.Matched, GroupFinderRefusalReason.None, entry.LastSentWaitingCount);
					return;
				}

				/* Attempted whether or not this server knows of an entry. A row can exist without
				 * one — this server restarted, or the character arrived from another server while
				 * still queued — and the player pressing Leave on a widget that is still showing
				 * it deserves to have it go away. */
				if (TryEnqueueAsyncWork(() => ProcessGroupFinderLeaveAsync(conn, characterID, guardKey), conn, characterID))
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
		/// Removes the character's waiting row and forgets them.
		/// </summary>
		private async Task ProcessGroupFinderLeaveAsync(NetworkConnection conn, long characterID, long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IGroupFinderQueueService>(out var queueService))
				{
					return;
				}

				DatabaseResult<bool> result = await queueService.DeleteAsync(characterID, onlyIfWaiting: true);

				TryEnqueueMainThread(() =>
				{
					if (result.IsSuccess && !result.Data &&
						groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry stillThere) &&
						stillThere.State == GroupFinderState.Waiting)
					{
						/* Nothing was waiting to delete, yet this server thinks they are waiting. The
						 * row was matched between the click and the delete. Leave the entry; the
						 * next pump reads the row and moves them. */
						return;
					}

					ForgetGroupFinderEntry(characterID);
					SendGroupFinderRefusal(conn, GroupFinderRefusalReason.Left);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error removing character {characterID} from the group finder: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Drops a queued character when they disconnect. Called from the character system hook.
		/// </summary>
		/// <remarks>
		/// A character being transferred into their instance was forgotten before the disconnect
		/// that moves them, so this only ever sees genuine departures. Their row is deleted
		/// whatever its state, and what the row said decides the rest: a character who was already
		/// matched holds a seat in a party they will never be moved into, and is taken out of it so
		/// the group is not left waiting on somebody who logged out. The delete reports the row it
		/// removed, so a match that landed on another server in the same instant is seen rather
		/// than raced.
		/// </remarks>
		private void CharacterSystem_OnGroupFinderCharacterDisconnected(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null || !groupFinderEntries.Remove(character.ID))
			{
				return;
			}

			long characterID = character.ID;
			if (!TryEnqueueAsyncWork(() => RemoveDisconnectedWaiterAsync(characterID), characterID))
			{
				Log.Warning("InteractableSystem", $"Group finder: could not enqueue removal of disconnected character {characterID}'s row; the stale sweep will reap it.");
			}
		}

		/// <summary>
		/// Removes a departed character's row and, if it had been matched, their party seat.
		/// </summary>
		private async Task RemoveDisconnectedWaiterAsync(long characterID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IGroupFinderQueueService>(out var queueService))
				{
					return;
				}

				DatabaseResult<GroupFinderQueueData?> removed = await queueService.DeleteReturningAsync(characterID);
				if (!removed.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Group finder could not delete disconnected character {characterID}'s queue row: {removed.ErrorCode} - {removed.ErrorMessage}");
					return;
				}

				if (!removed.Data.HasValue ||
					removed.Data.Value.Status != (int)GroupFinderQueueStatus.Matched ||
					removed.Data.Value.PartyID <= 0)
				{
					return;
				}

				long partyID = removed.Data.Value.PartyID;
				if (Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
				{
					await partySystem.RemoveCharacterFromPartyAsync(characterID, partyID, "matched by the group finder but logged out before being moved");
				}
				else
				{
					await Log.Warning("InteractableSystem",
						$"Group finder: character {characterID} logged out while matched into party {partyID} and could not be removed from it; the party system is unavailable.");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error removing disconnected group finder character {characterID}: {ex}");
			}
		}

		/// <summary>
		/// Deletes a character's queue row in any state, logging rather than reporting failure.
		/// </summary>
		private async Task DeleteGroupFinderRowAsync(long characterID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IGroupFinderQueueService>(out var queueService))
				{
					return;
				}

				DatabaseResult<bool> result = await queueService.DeleteAsync(characterID, onlyIfWaiting: false);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Group finder could not delete character {characterID}'s queue row: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error deleting group finder row for character {characterID}: {ex}");
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Pump
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Main-thread half of the pump: validates this server's waiters against what it can see
		/// locally, snapshots them, and hands the database work to a worker.
		/// </summary>
		/// <param name="deltaTime">Seconds since the last pump.</param>
		private void OnGroupFinderPump(float deltaTime)
		{
			if (Server == null || Interlocked.CompareExchange(ref groupFinderPumpInFlight, 0, 0) != 0)
			{
				return;
			}

			DateTime now = DateTime.UtcNow;
			bool sweepDue = now >= nextGroupFinderStaleSweepUtc;

			if (groupFinderEntries.Count == 0 && !sweepDue)
			{
				return;
			}

			var items = new List<GroupFinderPumpItem>(groupFinderEntries.Count);
			List<long> toForget = null;

			foreach (KeyValuePair<long, GroupFinderEntry> kvp in groupFinderEntries)
			{
				GroupFinderEntry entry = kvp.Value;

				bool alive = entry.Connection != null && entry.Connection.IsActive &&
					entry.Character != null && entry.Character.NetworkObject != null && entry.Character.NetworkObject.IsSpawned;
				if (!alive)
				{
					// The disconnect hook normally gets here first. Belt and braces.
					(toForget ??= new List<long>()).Add(kvp.Key);
					continue;
				}

				if (entry.State == GroupFinderState.Waiting)
				{
					/* Things the character did while waiting take them out of the queue: walking
					 * into a dungeon, accepting a party invitation, or walking away from the
					 * entrance. The first two the finder would have skipped at match time anyway;
					 * the third is the leash that keeps a transfer from surprising anybody. Telling
					 * them now, with the reason, is better than a panel that says "waiting" forever.
					 * The delete is conditional on the row still waiting — if it was matched a
					 * moment ago, the entry is kept and the match is honoured on the next pump. */
					bool inParty = entry.Character.TryGet(out IPartyController partyController) && partyController.ID != 0;
					GroupFinderRefusalReason cancel = GroupFinderRules.ResolveWaitingCancel(
						entry.Character.IsInInstance(), inParty, IsNearEntrance(entry));

					if (cancel != GroupFinderRefusalReason.None)
					{
						CancelWaitingEntry(entry, cancel);
						continue;
					}
				}

				float healthPCT = entry.Character.TryGet(out ICharacterAttributeController attributeController)
					? attributeController.GetHealthResourceAttributeCurrentPercentage()
					: 0.0f;

				items.Add(new GroupFinderPumpItem
				{
					Connection = entry.Connection,
					CharacterID = entry.CharacterID,
					WorldServerID = entry.WorldServerID,
					SceneName = entry.SceneName,
					Difficulty = entry.Difficulty,
					Capacity = entry.Capacity,
					GroupSize = entry.GroupSize,
					HealthPCT = healthPCT,
					BackfillDue = entry.State == GroupFinderState.Waiting && now >= entry.NextBackfillAttemptUtc,
				});
			}

			if (toForget != null)
			{
				foreach (long id in toForget)
				{
					groupFinderEntries.Remove(id);
					long characterID = id;
					TryEnqueueAsyncWork(() => DeleteGroupFinderRowAsync(characterID), characterID);
				}
			}

			if (items.Count == 0 && !sweepDue)
			{
				return;
			}

			if (sweepDue)
			{
				nextGroupFinderStaleSweepUtc = now.AddSeconds(groupFinderStaleSweepIntervalSeconds);
			}

			Interlocked.Exchange(ref groupFinderPumpInFlight, 1);
			if (!TryEnqueueAsyncWork(() => RunGroupFinderPumpAsync(items, sweepDue)))
			{
				Interlocked.Exchange(ref groupFinderPumpInFlight, 0);
			}
		}

		/// <summary>
		/// Whether a queued character is still within the leash of the entrance they queued at.
		/// Main thread only.
		/// </summary>
		/// <remarks>
		/// An entrance that has been destroyed — its scene unloaded — reads as "not near": there is
		/// no longer a door to be standing at, and the queue entry has nothing to be measured from.
		/// </remarks>
		private bool IsNearEntrance(GroupFinderEntry entry)
		{
			if (entry?.Entrance == null || entry.Character?.Transform == null)
			{
				return false;
			}

			Transform door = entry.Entrance.Transform;
			if (door == null)
			{
				return false;
			}

			float leashSqr = groupFinderLeashMeters * groupFinderLeashMeters;
			return (door.position - entry.Character.Transform.position).sqrMagnitude <= leashSqr;
		}

		/// <summary>
		/// Cancels a waiting entry for a reason the character caused, if its row is still waiting.
		/// </summary>
		private void CancelWaitingEntry(GroupFinderEntry entry, GroupFinderRefusalReason reason)
		{
			long characterID = entry.CharacterID;
			NetworkConnection conn = entry.Connection;

			TryEnqueueAsyncWork(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IGroupFinderQueueService>(out var queueService))
					{
						return;
					}

					DatabaseResult<bool> result = await queueService.DeleteAsync(characterID, onlyIfWaiting: true);
					if (result.IsSuccess && !result.Data)
					{
						// Matched in the meantime. The entry stays; the next pump moves them.
						return;
					}

					TryEnqueueMainThread(() =>
					{
						ForgetGroupFinderEntry(characterID);
						SendGroupFinderRefusal(conn, reason);
					});
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error cancelling group finder entry for character {characterID}: {ex}");
				}
			}, characterID);
		}

		/// <summary>
		/// Worker half of the pump: heartbeat, read back, act on matches, fill open runs, form
		/// groups, sweep.
		/// </summary>
		private async Task RunGroupFinderPumpAsync(List<GroupFinderPumpItem> items, bool sweepDue)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IGroupFinderQueueService>(out var queueService))
				{
					return;
				}

				if (sweepDue)
				{
					/* Rows twice the stale window old. Every scene server sweeps, whether or not
					 * it has waiters, because the rows that need sweeping belong to servers that
					 * are no longer here to do it. Bounded per call; a backlog drains over sweeps. */
					var sweepResult = await queueService.DeleteStaleAsync(DateTime.UtcNow.AddSeconds(-2.0 * groupFinderStalePulseSeconds), 256);
					if (sweepResult.IsSuccess && sweepResult.Data > 0)
					{
						await Log.Debug("InteractableSystem", $"Group finder swept {sweepResult.Data} stale queue rows.");
					}
				}

				if (items.Count == 0)
				{
					return;
				}

				var ids = new List<long>(items.Count);
				foreach (GroupFinderPumpItem item in items)
				{
					ids.Add(item.CharacterID);
				}

				await queueService.PulseAsync(ids);

				var rowsResult = await queueService.FetchByCharactersAsync(ids);
				if (!rowsResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"Group finder could not read its queue rows: {rowsResult.ErrorCode} - {rowsResult.ErrorMessage}");
					return;
				}

				var rowsByCharacter = new Dictionary<long, GroupFinderQueueData>(rowsResult.Data.Count);
				foreach (GroupFinderQueueData row in rowsResult.Data)
				{
					rowsByCharacter[row.CharacterID] = row;
				}

				// Waiters grouped by what they are waiting for, in queue order within each group.
				var waitingByKey = new Dictionary<(string, int), List<GroupFinderPumpItem>>();

				foreach (GroupFinderPumpItem item in items)
				{
					if (!rowsByCharacter.TryGetValue(item.CharacterID, out GroupFinderQueueData row))
					{
						/* No row. A sweep took it, or a server restart lost it. The widget must not
						 * keep saying "waiting" about a queue the character is not in. */
						long gone = item.CharacterID;
						NetworkConnection goneConn = item.Connection;
						TryEnqueueMainThread(() =>
						{
							if (groupFinderEntries.ContainsKey(gone))
							{
								ForgetGroupFinderEntry(gone);
								SendGroupFinderRefusal(goneConn, GroupFinderRefusalReason.Removed);
							}
						});
						continue;
					}

					if (row.Status == (int)GroupFinderQueueStatus.Matched)
					{
						await DispatchMatchedAsync(item.CharacterID, row.PartyID, row.InstanceID);
						continue;
					}

					var key = (item.SceneName, item.Difficulty);
					if (!waitingByKey.TryGetValue(key, out List<GroupFinderPumpItem> group))
					{
						group = new List<GroupFinderPumpItem>();
						waitingByKey[key] = group;
					}
					group.Add(item);
				}

				foreach (KeyValuePair<(string, int), List<GroupFinderPumpItem>> kvp in waitingByKey)
				{
					await ProcessWaitingGroupAsync(queueService, kvp.Key.Item1, kvp.Key.Item2, kvp.Value);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error in the group finder pump: {ex}");
			}
			finally
			{
				Interlocked.Exchange(ref groupFinderPumpInFlight, 0);
			}
		}

		/// <summary>
		/// For one dungeon at one difficulty: fill open runs first, then try to form a group,
		/// then tell whoever is still waiting how many are waiting.
		/// </summary>
		private async Task ProcessWaitingGroupAsync(IGroupFinderQueueService queueService, string sceneName, int difficulty, List<GroupFinderPumpItem> waiters)
		{
			if (waiters.Count == 0)
			{
				return;
			}

			long worldServerID = waiters[0].WorldServerID;
			int capacity = waiters[0].Capacity;
			int groupSize = waiters[0].GroupSize;

			var stillWaiting = new List<GroupFinderPumpItem>(waiters.Count);

			/* Late-join first. A run somebody already opened to others is a group that exists now,
			 * and the player who opened it is waiting for exactly this. Only waiters whose retry
			 * timer has passed are offered one; a refusal — usually the party filling between the
			 * list and the join — backs that waiter off rather than hammering the same run. */
			bool anyBackfillDue = false;
			foreach (GroupFinderPumpItem waiter in waiters)
			{
				if (waiter.BackfillDue)
				{
					anyBackfillDue = true;
					break;
				}
			}

			IReadOnlyList<SceneData> openRuns = null;
			if (anyBackfillDue &&
				Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				var listResult = await sceneService.FetchJoinableInstancesAsync(
					worldServerID,
					sceneName,
					difficulty,
					(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
					capacity,
					MaxListedInstances);
				if (listResult.IsSuccess)
				{
					openRuns = listResult.Data;
				}
			}

			Dictionary<long, int> roomByInstance = null;
			if (openRuns != null && openRuns.Count > 0)
			{
				roomByInstance = new Dictionary<long, int>(openRuns.Count);
				foreach (SceneData run in openRuns)
				{
					/* Only runs with a party behind them. An ungrouped opener's run has nobody to
					 * add a joiner to — the same rule the entrance's Join applies. Pending and
					 * Loading runs have no occupants yet; a Ready run's count lags a pulse, which
					 * is the soft cap the whole entry path already accepts. */
					if (run.PartyID <= 0 || !IsUsableInstance(run, worldServerID))
					{
						continue;
					}
					int room = (SceneStatus)run.SceneStatus == SceneStatus.Ready ? capacity - run.CharacterCount : capacity;
					if (room > 0)
					{
						roomByInstance[run.ID] = room;
					}
				}
			}

			foreach (GroupFinderPumpItem waiter in waiters)
			{
				bool placed = false;

				if (waiter.BackfillDue && roomByInstance != null && roomByInstance.Count > 0)
				{
					foreach (SceneData run in openRuns)
					{
						if (!roomByInstance.TryGetValue(run.ID, out int room) || room <= 0)
						{
							continue;
						}

						if (await TryLateJoinAsync(queueService, waiter, run))
						{
							roomByInstance[run.ID] = room - 1;
							placed = true;
							break;
						}
					}

					if (!placed)
					{
						long characterID = waiter.CharacterID;
						TryEnqueueMainThread(() =>
						{
							if (groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry entry))
							{
								entry.NextBackfillAttemptUtc = DateTime.UtcNow.AddSeconds(groupFinderBackfillRetrySeconds);
							}
						});
					}
				}

				if (!placed)
				{
					stillWaiting.Add(waiter);
				}
			}

			if (stillWaiting.Count == 0)
			{
				return;
			}

			var countResult = await queueService.CountWaitingAsync(worldServerID, sceneName, difficulty, GroupFinderStaleBefore);
			if (!countResult.IsSuccess)
			{
				return;
			}
			int waiting = countResult.Data;

			if (waiting >= groupSize)
			{
				var formResult = await queueService.TryFormGroupAsync(
					worldServerID,
					sceneName,
					difficulty,
					groupSize,
					GroupFinderStaleBefore,
					(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
					(byte)PartyRank.Leader,
					(byte)PartyRank.Member);

				if (!formResult.IsSuccess)
				{
					/* A rollback is reported as a failure — a member joined a party between the
					 * select and the insert, or somebody's instance guard fired. Not an error in
					 * the pump; the next pump tries again without them. */
					await Log.Debug("InteractableSystem",
						$"Group finder did not form a group for '{sceneName}' at difficulty {difficulty}: {formResult.ErrorCode} - {formResult.ErrorMessage}");
				}
				else if (formResult.Data.Formed)
				{
					GroupFinderMatchData match = formResult.Data;
					await Log.Debug("InteractableSystem",
						$"Group finder formed party {match.PartyID} of {match.MemberCharacterIDs.Count} for '{sceneName}' at difficulty {difficulty}; instance {match.InstanceID}.");

					/* This server's own members are moved now rather than on the next pump. The
					 * other members' servers see the matched rows on theirs. */
					var placedHere = new HashSet<long>();
					foreach (long memberID in match.MemberCharacterIDs)
					{
						foreach (GroupFinderPumpItem waiter in stillWaiting)
						{
							if (waiter.CharacterID == memberID)
							{
								placedHere.Add(memberID);
								await DispatchMatchedAsync(memberID, match.PartyID, match.InstanceID);
								break;
							}
						}
					}

					stillWaiting.RemoveAll(w => placedHere.Contains(w.CharacterID));
					waiting = Math.Max(0, waiting - match.MemberCharacterIDs.Count);
				}
			}

			int reportedWaiting = waiting;
			foreach (GroupFinderPumpItem waiter in stillWaiting)
			{
				long characterID = waiter.CharacterID;
				TryEnqueueMainThread(() =>
				{
					if (!groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry entry) ||
						entry.State != GroupFinderState.Waiting)
					{
						return;
					}

					// Only when the number moves. The widget keeps showing the last one.
					if (entry.LastSentWaitingCount != reportedWaiting)
					{
						SendGroupFinderStatus(entry.Connection, entry, GroupFinderState.Waiting, GroupFinderRefusalReason.None, reportedWaiting);
					}
				});
			}
		}

		/// <summary>
		/// Places one waiter into one open run: claims their row, then joins the run's party.
		/// </summary>
		/// <remarks>
		/// The row is claimed before the party is joined, so a group forming on another server at
		/// the same instant cannot also take this character. If the party refuses — full, being
		/// changed, dissolved — the claim is released and the waiter goes back to waiting; nothing
		/// about them has moved.
		/// </remarks>
		/// <returns>True when the character is in the run's party and their row names the run.</returns>
		private async Task<bool> TryLateJoinAsync(IGroupFinderQueueService queueService, GroupFinderPumpItem waiter, SceneData run)
		{
			if (!Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
			{
				return false;
			}

			DatabaseResult<bool> claim = await queueService.TryClaimForInstanceAsync(waiter.CharacterID, run.PartyID, run.ID);
			if (!claim.IsSuccess || !claim.Data)
			{
				return false;
			}

			if (!await partySystem.TryAddCharacterToPartyAsync(waiter.Connection, waiter.CharacterID, run.PartyID, waiter.HealthPCT))
			{
				DatabaseResult<bool> release = await queueService.ReleaseClaimAsync(waiter.CharacterID, run.ID);
				if (!release.IsSuccess || !release.Data)
				{
					await Log.Warning("InteractableSystem",
						$"Group finder could not release character {waiter.CharacterID}'s claim on instance {run.ID} after the party refused them; the row will be corrected by the next pump or the sweep.");
				}
				return false;
			}

			await Log.Debug("InteractableSystem",
				$"Group finder placed character {waiter.CharacterID} into open instance {run.ID} (party {run.PartyID}) of '{run.SceneName}'.");

			await DispatchMatchedAsync(waiter.CharacterID, run.PartyID, run.ID);
			return true;
		}

		/// <summary>
		/// Reads the matched character's party rank and hands the transfer to the main thread.
		/// </summary>
		/// <remarks>
		/// The rank is read here rather than carried on the queue row because it is the party's to
		/// change: leadership may already have moved by the time a member on a slow server is
		/// transferred, and what they are told on the way out should be what is true.
		/// </remarks>
		private async Task DispatchMatchedAsync(long characterID, long partyID, long instanceID)
		{
			PartyRank rank = PartyRank.Member;
			if (Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
			{
				DatabaseResult<CharacterPartyData?> membership = await charPartyService.FetchAsync(characterID);
				if (membership.IsSuccess && membership.Data.HasValue && membership.Data.Value.PartyID == partyID)
				{
					rank = (PartyRank)membership.Data.Value.Rank;
				}
			}

			TryEnqueueMainThread(() => HandleMatchedEntry(characterID, partyID, instanceID, rank));
		}

		/// <summary>
		/// Moves a matched character into their instance, or decides to wait, or gives up on them.
		/// Main thread only.
		/// </summary>
		private void HandleMatchedEntry(long characterID, long partyID, long instanceID, PartyRank rank)
		{
			if (!groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry entry))
			{
				return;
			}

			DateTime now = DateTime.UtcNow;

			if (entry.State != GroupFinderState.Matched)
			{
				entry.State = GroupFinderState.Matched;
				entry.MatchedAtUtc = now;
				entry.MatchedPartyID = partyID;
				entry.MatchedInstanceID = instanceID;
				SendGroupFinderStatus(entry.Connection, entry, GroupFinderState.Matched, GroupFinderRefusalReason.None, entry.GroupSize);
			}

			NetworkConnection conn = entry.Connection;
			IPlayerCharacter character = entry.Character;

			bool alive = conn != null && conn.IsActive &&
				character != null && character.NetworkObject != null && character.NetworkObject.IsSpawned;
			if (!alive)
			{
				groupFinderEntries.Remove(characterID);
				TryEnqueueAsyncWork(() => DeleteGroupFinderRowAsync(characterID), characterID);
				return;
			}

			/* At the door, as well as free to travel. A matched player who stepped outside the
			 * leash is not moved until they step back in — the transfer must never surprise
			 * somebody who has walked off — and the grace bounds how long the group waits. */
			bool canTransfer = !character.IsInInstance() &&
				CharacterStateValidation.CanActOrMove(character) &&
				IsNearEntrance(entry);

			switch (GroupFinderRules.ResolveMatchedTransfer(canTransfer, (now - entry.MatchedAtUtc).TotalSeconds, groupFinderTransferGraceSeconds))
			{
				case GroupFinderRules.MatchedTransferAction.Wait:
					return;

				case GroupFinderRules.MatchedTransferAction.GiveUp:
					GiveUpOnMatchedEntry(entry);
					return;
			}

			/* The party is made true on this side before the hand-off, exactly as the entrance's
			 * Join does. The arrival load re-reads membership from the database anyway, but the
			 * client's own controller is what the panel it opens on arrival consults first. A
			 * late-join arrives here with this already done by the party system; it is not
			 * repeated, because a second add would double the roster row on the client. */
			if (character.TryGet(out IPartyController partyController) && partyController.ID != partyID)
			{
				partyController.ID = partyID;
				partyController.Rank = rank;

				if (Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
				{
					partySystem.AddPartyCharacterTracker(partyID, characterID);
				}

				float healthPCT = character.TryGet(out ICharacterAttributeController attributeController)
					? attributeController.GetHealthResourceAttributeCurrentPercentage()
					: 0.0f;

				Server.NetworkWrapper.Broadcast(conn, new PartyAddBroadcast()
				{
					PartyID = partyID,
					CharacterID = characterID,
					Rank = rank,
					HealthPCT = healthPCT,
				}, true, FishNet.Transporting.Channel.Reliable);
			}

			if (entry.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(entry.AchievementTemplate, 1);
			}

			CharacterRespawnPositionDetails respawnDetails = entry.SceneDetails.RespawnPositions.Values.ToList().GetRandom();

			/* Forgotten before the disconnect, so the disconnect hook sees a departure it has no
			 * entry for and leaves the row alone — the delete below is this path's, and it does
			 * not depend on any status. */
			groupFinderEntries.Remove(characterID);
			TryEnqueueAsyncWork(() => DeleteGroupFinderRowAsync(characterID), characterID);

			Log.Debug("InteractableSystem", $"Group finder: moving character {characterID} into instance {instanceID} (party {partyID}).");

			// No created instance to release: the group's instance belongs to the group, not to this transfer.
			EnterInstance(conn, character, instanceID, respawnDetails, 0);
		}

		/// <summary>
		/// Takes a matched character who never became free to travel out of the group and the queue.
		/// </summary>
		private void GiveUpOnMatchedEntry(GroupFinderEntry entry)
		{
			long characterID = entry.CharacterID;
			long partyID = entry.MatchedPartyID;
			NetworkConnection conn = entry.Connection;

			groupFinderEntries.Remove(characterID);

			Log.Debug("InteractableSystem", $"Group finder: character {characterID} stayed untransferable, or away from the entrance, past the grace; their group goes on without them.");

			TryEnqueueAsyncWork(async () =>
			{
				try
				{
					if (Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
					{
						await partySystem.RemoveCharacterFromPartyAsync(characterID, partyID, "matched by the group finder but never became free to travel");
					}

					await DeleteGroupFinderRowAsync(characterID);

					TryEnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive || conn.FirstObject == null)
						{
							return;
						}

						IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
						if (partyController != null && partyController.ID == partyID)
						{
							partyController.ID = 0;
							partyController.Rank = PartyRank.None;
							Server?.NetworkWrapper?.Broadcast(conn, new PartyLeaveBroadcast(), true, FishNet.Transporting.Channel.Reliable);
						}

						SendGroupFinderRefusal(conn, GroupFinderRefusalReason.GroupLeftWithoutYou);
					});
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error giving up on matched character {characterID}: {ex}");
				}
			}, characterID);
		}

		/// <summary>
		/// Forgets a local entry without touching its row. Main thread only.
		/// </summary>
		private void ForgetGroupFinderEntry(long characterID)
		{
			groupFinderEntries.Remove(characterID);
		}

		// ──────────────────────────────────────────────────────────────────
		//  Replies
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Tells a client it is not in the queue, and why. Main thread only.
		/// </summary>
		private void SendGroupFinderRefusal(NetworkConnection conn, GroupFinderRefusalReason reason)
		{
			if (conn == null || !conn.IsActive || Server?.NetworkWrapper == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new GroupFinderStatusBroadcast()
			{
				State = GroupFinderState.None,
				Reason = reason,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>
		/// Tells a client where it stands with the finder. Main thread only.
		/// </summary>
		private void SendGroupFinderStatus(NetworkConnection conn, GroupFinderEntry entry, GroupFinderState state, GroupFinderRefusalReason reason, int waitingCount)
		{
			if (conn == null || !conn.IsActive || Server?.NetworkWrapper == null || entry == null)
			{
				return;
			}

			entry.LastSentWaitingCount = waitingCount;

			Server.NetworkWrapper.Broadcast(conn, new GroupFinderStatusBroadcast()
			{
				State = state,
				DungeonTemplateID = entry.DungeonTemplateID,
				SceneName = entry.SceneName ?? string.Empty,
				Difficulty = entry.Difficulty,
				WaitingCount = waitingCount,
				GroupSize = entry.GroupSize,
				Reason = reason,
			}, true, FishNet.Transporting.Channel.Reliable);
		}
	}
}
