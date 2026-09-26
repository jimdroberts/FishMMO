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

			/// <summary>Dungeon (<see cref="SceneType.Group"/>) or arena (<see cref="SceneType.PvP"/>) queue.</summary>
			public SceneType Kind = SceneType.Group;

			public string SceneName;
			public int DungeonTemplateID;

			/// <summary>Arena template, for arena entries.</summary>
			public int ArenaTemplateID;

			/// <summary>Teams and seats per team, for arena entries.</summary>
			public int TeamCount;
			public int TeamSize;

			/// <summary>Difficulty index for dungeons; format index for arenas.</summary>
			public int Difficulty;
			public int Capacity;

			/// <summary>Players needed: the finder's group size, or the arena's full match size.</summary>
			public int GroupSize;
			public WorldSceneDetails SceneDetails;
			public AchievementTemplate AchievementTemplate;

			/// <summary>The entrance they queued at; the leash is measured from it.</summary>
			public IInteractable Entrance;

			/// <summary>What this server last told the client.</summary>
			public GroupFinderState State;

			/// <summary>Last waiting count sent, so the pump only speaks when the number moves.</summary>
			public int LastSentWaitingCount = -1;

			/// <summary>
			/// When this server first saw the row matched, for the transfer grace, in
			/// <see cref="MonotonicClock"/> seconds. Every time on an entry is a local duration of
			/// this server's; the queue row's own times are the database's (DbNow).
			/// </summary>
			public double MatchedAt;

			/// <summary>Party the row was matched into.</summary>
			public long MatchedPartyID;

			/// <summary>Instance the row was matched into.</summary>
			public long MatchedInstanceID;

			/// <summary>
			/// Earliest time the pump may try to late-join this waiter into an open run, in
			/// <see cref="MonotonicClock"/> seconds; positive infinity for never.
			/// </summary>
			public double NextBackfillAttemptAt;

			/// <summary>
			/// When this server registered the entry, for the arena's widening rating band, in
			/// <see cref="MonotonicClock"/> seconds.
			/// </summary>
			public double QueuedAt;

			/// <summary>Arena: whether the format is ranked.</summary>
			public bool Ranked;

			/// <summary>Arena: balance teams by rating when composing.</summary>
			public bool BalanceTeams;

			/// <summary>Arena: rating band parameters, from the template.</summary>
			public int RatingBandBase;
			public int RatingBandGrowth;
			public int RatingBandMax;

			/// <summary>Arena: template id of the PvP Rank attribute, for unranked balancing; 0 when unresolved.</summary>
			public int RankAttributeTemplateID;

			/// <summary>
			/// The player asked to leave and the delete failed. The pump keeps retrying it as a
			/// cancel with <see cref="GroupFinderRefusalReason.Left"/>; until it lands the row is
			/// still this server's to pulse and to honour if it is matched.
			/// </summary>
			public bool LeaveRequested;

			/// <summary>
			/// A conditional delete found no waiting row to remove: it was matched, or it is gone.
			/// The next pump reads the row instead of cancelling again, so a match is moved and a
			/// missing row is reported rather than the cancel repeating forever.
			/// </summary>
			public bool RowCheckDue;
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
			public SceneType Kind;
			public string SceneName;
			public int ArenaTemplateID;
			public int TeamCount;
			public int TeamSize;
			public int Difficulty;
			public int Capacity;
			public int GroupSize;
			public float HealthPCT;
			public bool BackfillDue;
			public double QueuedAt;
			public bool Ranked;
			public bool BalanceTeams;
			public int RatingBandBase;
			public int RatingBandGrowth;
			public int RatingBandMax;
			public int RankAttributeTemplateID;
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

		/// <summary>
		/// Next time the stale-row sweep runs, in <see cref="MonotonicClock"/> seconds. Runs whether
		/// or not anybody is queued here.
		/// </summary>
		private double nextGroupFinderStaleSweepAt;

		/// <summary>
		/// How old a heartbeat may be before it counts as stopped.
		/// </summary>
		/// <remarks>
		/// An age, not a moment. The database stamps every heartbeat and applies this against its
		/// own clock, because the row being judged was usually pulsed by a different scene server:
		/// a moment computed here compared this machine's clock with that one's, so a server running
		/// behind had its waiters left out of every count and, far enough behind, swept.
		/// </remarks>
		private TimeSpan GroupFinderStaleAfter => TimeSpan.FromSeconds(groupFinderStalePulseSeconds);

		/// <summary>
		/// Registers the group finder's requests and pump. Called from InitializeOnce.
		/// </summary>
		private void InitializeGroupFinder()
		{
			groupFinderEntries.Clear();
			Interlocked.Exchange(ref groupFinderPumpInFlight, 0);
			nextGroupFinderStaleSweepAt = MonotonicClock.NowSeconds;

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

			InitializeArena();
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

			DeinitializeArena();

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
					await Log.Warning("InteractableSystem", $"Group finder could not read the instances held by character {characterID}: [{heldResult.ErrorCode}] {heldResult.ErrorMessage}");
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
					worldServerID, characterID, DbSceneType(SceneType.Group), dungeonName, difficultyIndex, GroupFinderStaleAfter);
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

				var countResult = await queueService.CountWaitingAsync(worldServerID, DbSceneType(SceneType.Group), dungeonName, difficultyIndex, GroupFinderStaleAfter);
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
						NextBackfillAttemptAt = MonotonicClock.NowSeconds,
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
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"Group finder could not remove character {characterID}'s queue row on leave; the pump retries it: [{result.ErrorCode}] {result.ErrorMessage}");
				}

				TryEnqueueMainThread(() =>
				{
					/* A failed delete is not a leave. The row is still there and still matchable by
					 * every server's pump, so forgetting the entry here — as this used to — stopped
					 * the pulse and the dispatch while leaving the row in the queue: a group could
					 * form around a player who had been told they left, and wait for a transfer
					 * nobody would ever make. The entry is kept and the pump retries the leave. */
					if (!result.IsSuccess)
					{
						if (groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry pending) &&
							pending.State == GroupFinderState.Waiting)
						{
							pending.LeaveRequested = true;
						}
						else
						{
							SendGroupFinderRefusal(conn, GroupFinderRefusalReason.ServerError);
						}
						return;
					}

					if (!result.Data &&
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

			/* EnqueuePersistence, not TryEnqueueAsyncWork: the entry is already forgotten and there
			 * is nobody left to tell about a refusal, so a dropped removal would leave a matched
			 * character's party seat held by somebody who logged out. */
			long characterID = character.ID;
			EnqueuePersistence(() => RemoveDisconnectedWaiterAsync(characterID), characterID);
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
						$"Group finder could not delete disconnected character {characterID}'s queue row; the stale sweep will reap it: [{removed.ErrorCode}] {removed.ErrorMessage}");
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
					if (!await partySystem.RemoveCharacterFromPartyAsync(characterID, partyID, "matched by the group finder but logged out before being moved"))
					{
						await Log.Warning("InteractableSystem",
							$"Group finder: character {characterID} logged out while matched into party {partyID} and could not be removed from it; the party keeps an absent member.");
					}
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

			double now = MonotonicClock.NowSeconds;
			bool sweepDue = now >= nextGroupFinderStaleSweepAt;

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

				if (entry.State == GroupFinderState.Waiting && entry.RowCheckDue)
				{
					/* The last cancel found no waiting row to delete. This pump reads the row
					 * instead of cancelling again: a matched row is moved, a missing one reported.
					 * Cancelling again would find the same nothing, every pump, and the match it was
					 * honouring would never be read. */
					entry.RowCheckDue = false;
				}
				else if (entry.State == GroupFinderState.Waiting)
				{
					/* Things the character did while waiting take them out of the queue: walking
					 * into a dungeon, accepting a party invitation, or walking away from the
					 * entrance. The first two the finder would have skipped at match time anyway;
					 * the third is the leash that keeps a transfer from surprising anybody. Telling
					 * them now, with the reason, is better than a panel that says "waiting" forever.
					 * The delete is conditional on the row still waiting — if it was matched a
					 * moment ago, the entry is kept and the match is honoured on the next pump. */
					/* A party is a reason to leave the dungeon queue and the way to be in the arena
					 * queue: pre-made groups queue for arenas together. */
					bool inParty = entry.Kind == SceneType.Group &&
						entry.Character.TryGet(out IPartyController partyController) && partyController.ID != 0;
					GroupFinderRefusalReason cancel = entry.LeaveRequested
						? GroupFinderRefusalReason.Left
						: GroupFinderRules.ResolveWaitingCancel(entry.Character.IsInInstance(), inParty, IsNearEntrance(entry));

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
					Kind = entry.Kind,
					SceneName = entry.SceneName,
					ArenaTemplateID = entry.ArenaTemplateID,
					TeamCount = entry.TeamCount,
					TeamSize = entry.TeamSize,
					Difficulty = entry.Difficulty,
					Capacity = entry.Capacity,
					GroupSize = entry.GroupSize,
					HealthPCT = healthPCT,
					BackfillDue = entry.State == GroupFinderState.Waiting && now >= entry.NextBackfillAttemptAt,
					QueuedAt = entry.QueuedAt,
					Ranked = entry.Ranked,
					BalanceTeams = entry.BalanceTeams,
					RatingBandBase = entry.RatingBandBase,
					RatingBandGrowth = entry.RatingBandGrowth,
					RatingBandMax = entry.RatingBandMax,
					RankAttributeTemplateID = entry.RankAttributeTemplateID,
				});
			}

			if (toForget != null)
			{
				foreach (long id in toForget)
				{
					groupFinderEntries.Remove(id);
					long characterID = id;
					EnqueuePersistence(() => DeleteGroupFinderRowAsync(characterID), characterID);
				}
			}

			if (items.Count == 0 && !sweepDue)
			{
				return;
			}

			if (sweepDue)
			{
				nextGroupFinderStaleSweepAt = now + groupFinderStaleSweepIntervalSeconds;
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

			EnqueuePersistence(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IGroupFinderQueueService>(out var queueService))
					{
						return;
					}

					/* A failed delete keeps the entry. Forgetting it, as this used to, told the
					 * player they were out while the row stayed in the queue — unpulsed but still
					 * matchable for the stale window — so a group could form around somebody who had
					 * walked away. Kept, the next pump sees the same reason and tries again. */
					DatabaseResult<bool> result = await queueService.DeleteAsync(characterID, onlyIfWaiting: true);
					if (!result.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"Group finder could not cancel character {characterID}'s queue row ({reason}); retrying next pump: [{result.ErrorCode}] {result.ErrorMessage}");
						return;
					}

					if (!result.Data)
					{
						/* Nothing waiting to delete: matched in the meantime, or the row is gone. The
						 * entry stays, and the next pump reads the row rather than cancelling again —
						 * a match is moved (a leave that lost the race to a match is refused, as the
						 * leave handler refuses one), and a missing row is reported. */
						TryEnqueueMainThread(() =>
						{
							if (groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry stillThere))
							{
								stillThere.LeaveRequested = false;
								stillThere.RowCheckDue = true;
							}
						});
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
		/// <remarks>
		/// <para>
		/// The fixed cost of a pump is a handful of statements however many waiters and keys this
		/// server has: one heartbeat that returns the rows (and, for matched rows, the party
		/// membership the transfer checks), one count across every key, and one read of where arena
		/// seats may be backfilled. Only forming, late-joining and backfilling remain per key, and
		/// the backfill transaction runs only for a key that read says has an opening. It used to
		/// be a round trip per matched row, per key and per arena key on every pump — about fifty,
		/// in series, for thirty matched players across ten keys.
		/// </para>
		/// </remarks>
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
					var sweepResult = await queueService.DeleteStaleAsync(TimeSpan.FromSeconds(2.0 * groupFinderStalePulseSeconds), 256);
					if (!sweepResult.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"Group finder could not sweep stale queue rows: [{sweepResult.ErrorCode}] {sweepResult.ErrorMessage}");
					}
					else if (sweepResult.Data > 0)
					{
						await Log.Debug("InteractableSystem", $"Group finder swept {sweepResult.Data} stale queue rows.");
					}

					/* Arena matches whose instance is gone but whose row never reached Ended — a
					 * hosting server that died, a load that failed — would hold every seat out of
					 * both finders forever. Ten minutes is far longer than any gathering or match
					 * takes to reach a hosting server.
					 *
					 * Every scene server runs this, deliberately not one elected per world: the
					 * matches it exists for are the ones whose own server is gone, and an elected
					 * sweeper is one more server that can be gone. What made it expensive was the
					 * table, not the servers — the sweep read all of a history nothing deletes — and
					 * it now reads only the unfinished matches, through their own partial index. */
					if (Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var arenaService))
					{
						var cancelResult = await arenaService.CancelAbandonedAsync(TimeSpan.FromMinutes(10), 64);
						if (!cancelResult.IsSuccess)
						{
							await Log.Warning("InteractableSystem", $"Arena: could not cancel abandoned matches: [{cancelResult.ErrorCode}] {cancelResult.ErrorMessage}");
						}
						else if (cancelResult.Data > 0)
						{
							await Log.Warning("InteractableSystem", $"Arena: cancelled {cancelResult.Data} abandoned matches whose instances no longer exist.");
						}
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

				/* The heartbeat and the read-back are one statement. A pulse that keeps failing lets
				 * this server's rows go stale and be swept, and the waiters are then told they were
				 * removed — which should not be the first anybody hears of it — so it is logged. */
				var pulseResult = await queueService.PulseAsync(ids);
				if (!pulseResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"Group finder could not pulse and read its {ids.Count} queue rows: [{pulseResult.ErrorCode}] {pulseResult.ErrorMessage}");
					return;
				}

				var rowsByCharacter = new Dictionary<long, GroupFinderPulseData>(pulseResult.Data.Count);
				foreach (GroupFinderPulseData pulsed in pulseResult.Data)
				{
					rowsByCharacter[pulsed.Row.CharacterID] = pulsed;
				}

				// Waiters grouped by what they are waiting for, in queue order within each group.
				var waitingByKey = new Dictionary<(SceneType, string, int), List<GroupFinderPumpItem>>();

				foreach (GroupFinderPumpItem item in items)
				{
					if (!rowsByCharacter.TryGetValue(item.CharacterID, out GroupFinderPulseData pulsed))
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

					GroupFinderQueueData row = pulsed.Row;
					if (row.Status == (int)GroupFinderQueueStatus.Matched)
					{
						/* The membership came with the row, so a match waiting on its player — in
						 * combat, dead, away from the door — costs no read of its own each pump. */
						if (GroupFinderRules.IsMatchHonoured(row.PartyID, pulsed.MemberPartyID))
						{
							DispatchMatched(item.CharacterID, row.PartyID, row.InstanceID, row.PartyID > 0 ? (PartyRank)pulsed.MemberRank : PartyRank.Member);
						}
						else
						{
							await RemoveUnhonouredMatchAsync(item.CharacterID, row.PartyID, row.InstanceID);
						}
						continue;
					}

					var key = (item.Kind, item.SceneName, item.Difficulty);
					if (!waitingByKey.TryGetValue(key, out List<GroupFinderPumpItem> group))
					{
						group = new List<GroupFinderPumpItem>();
						waitingByKey[key] = group;
					}
					group.Add(item);
				}

				if (waitingByKey.Count == 0)
				{
					return;
				}

				/* One count for every key, per world server (in practice one: a scene server serves
				 * one world). A count that fails forms nothing this pump — the forming decision needs
				 * it — but late-joins and backfills, which do not, still run. */
				var countsByWorld = new Dictionary<long, IReadOnlyDictionary<GroupFinderQueueKey, int>>();
				var keysByWorld = new Dictionary<long, List<GroupFinderQueueKey>>();
				bool anyArena = false;
				foreach (KeyValuePair<(SceneType, string, int), List<GroupFinderPumpItem>> kvp in waitingByKey)
				{
					long world = kvp.Value[0].WorldServerID;
					if (!keysByWorld.TryGetValue(world, out List<GroupFinderQueueKey> worldKeys))
					{
						worldKeys = new List<GroupFinderQueueKey>();
						keysByWorld[world] = worldKeys;
					}
					worldKeys.Add(new GroupFinderQueueKey((int)kvp.Key.Item1, kvp.Key.Item2, kvp.Key.Item3));
					anyArena |= kvp.Key.Item1 == SceneType.PvP;
				}
				foreach (KeyValuePair<long, List<GroupFinderQueueKey>> kvp in keysByWorld)
				{
					var countResult = await queueService.CountWaitingAsync(kvp.Key, kvp.Value, GroupFinderStaleAfter);
					if (countResult.IsSuccess)
					{
						countsByWorld[kvp.Key] = countResult.Data;
					}
					else
					{
						await Log.Warning("InteractableSystem", $"Group finder could not count the waiters of {kvp.Value.Count} queues on world {kvp.Key}; no group is formed there this pump: [{countResult.ErrorCode}] {countResult.ErrorMessage}");
					}
				}

				/* Where an arena seat can be backfilled, read once and without locks. A key it does
				 * not name skips the backfill transaction, which locks and scans and used to run for
				 * every arena key on every pump. If the read fails, every key is tried as before:
				 * this is a shortcut past the transaction, never a gate on it. */
				Dictionary<long, HashSet<GroupFinderQueueKey>> openingsByWorld = null;
				if (anyArena)
				{
					openingsByWorld = new Dictionary<long, HashSet<GroupFinderQueueKey>>();
					foreach (long world in keysByWorld.Keys)
					{
						var openings = await queueService.FetchBackfillOpeningsAsync(world);
						if (openings.IsSuccess)
						{
							openingsByWorld[world] = new HashSet<GroupFinderQueueKey>(openings.Data);
						}
						else
						{
							await Log.Warning("InteractableSystem", $"Arena: could not read where seats may be backfilled on world {world}; trying every arena this pump: [{openings.ErrorCode}] {openings.ErrorMessage}");
						}
					}
				}

				foreach (KeyValuePair<(SceneType, string, int), List<GroupFinderPumpItem>> kvp in waitingByKey)
				{
					long world = kvp.Value[0].WorldServerID;
					var key = new GroupFinderQueueKey((int)kvp.Key.Item1, kvp.Key.Item2, kvp.Key.Item3);
					int? waiting = countsByWorld.TryGetValue(world, out IReadOnlyDictionary<GroupFinderQueueKey, int> counts) &&
						counts.TryGetValue(key, out int count)
						? count
						: (int?)null;

					if (kvp.Key.Item1 == SceneType.PvP)
					{
						bool backfillOpen = openingsByWorld == null ||
							!openingsByWorld.TryGetValue(world, out HashSet<GroupFinderQueueKey> open) ||
							open.Contains(key);
						await ProcessWaitingArenaGroupAsync(queueService, kvp.Key.Item2, kvp.Key.Item3, kvp.Value, waiting, backfillOpen);
					}
					else
					{
						await ProcessWaitingGroupAsync(queueService, kvp.Key.Item2, kvp.Key.Item3, kvp.Value, waiting);
					}
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
		/// <param name="waitingCount">
		/// The live waiters under this key, counted by the pump for every key at once before any of
		/// them was processed; null when that count failed, and then no group is formed this pump.
		/// </param>
		private async Task ProcessWaitingGroupAsync(IGroupFinderQueueService queueService, string sceneName, int difficulty, List<GroupFinderPumpItem> waiters, int? waitingCount)
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
								entry.NextBackfillAttemptAt = MonotonicClock.NowSeconds + groupFinderBackfillRetrySeconds;
							}
						});
					}
				}

				if (!placed)
				{
					stillWaiting.Add(waiter);
				}
			}

			if (stillWaiting.Count == 0 || !waitingCount.HasValue)
			{
				return;
			}

			/* The count was taken before the late-joins above. Each one claimed a waiting row of
			 * this key, so the count comes down by exactly the number placed. */
			int waiting = Math.Max(0, waitingCount.Value - (waiters.Count - stillWaiting.Count));

			if (waiting >= groupSize)
			{
				var formResult = await queueService.TryFormGroupAsync(
					worldServerID,
					sceneName,
					difficulty,
					groupSize,
					GroupFinderStaleAfter,
					(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
					(byte)PartyRank.Leader,
					(byte)PartyRank.Member);

				if (!formResult.IsSuccess)
				{
					/* A rollback is reported as a failure — a member joined a party between the
					 * select and the insert, or somebody's instance guard fired. Not an error in
					 * the pump; the next pump tries again without them. Those carry StaleState;
					 * anything else is the database failing, and is logged as such. */
					await LogMatchmakingFailureAsync(formResult, $"Group finder did not form a group for '{sceneName}' at difficulty {difficulty}");
				}
				else if (formResult.Data.Formed)
				{
					GroupFinderMatchData match = formResult.Data;
					await Log.Debug("InteractableSystem",
						$"Group finder formed party {match.PartyID} of {match.MemberCharacterIDs.Count} for '{sceneName}' at difficulty {difficulty}; instance {match.InstanceID}.");

					/* This server's own members are moved now rather than on the next pump. The
					 * other members' servers see the matched rows on theirs.
					 *
					 * Their ranks are the ones the transaction just wrote, so nothing is read: the
					 * membership check a later pump makes exists for a match that has waited, and
					 * this one has not. */
					var placedHere = new HashSet<long>();
					foreach (long memberID in match.MemberCharacterIDs)
					{
						foreach (GroupFinderPumpItem waiter in stillWaiting)
						{
							if (waiter.CharacterID == memberID)
							{
								placedHere.Add(memberID);
								DispatchMatched(memberID, match.PartyID, match.InstanceID, memberID == match.LeaderCharacterID ? PartyRank.Leader : PartyRank.Member);
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
		/// Logs a matchmaking transaction that did not complete, at the level it deserves.
		/// </summary>
		/// <remarks>
		/// The forming and backfill transactions roll back with <see cref="DatabaseErrorCodes.StaleState"/>
		/// when somebody's eligibility changed under the lock — routine under concurrency, and the
		/// next pump tries again without them. Anything else is the database failing, and used to be
		/// logged at Debug alongside the routine case, where nobody would see it.
		/// </remarks>
		/// <param name="result">The failed result.</param>
		/// <param name="what">What was not done, for the log line.</param>
		private static Task LogMatchmakingFailureAsync<T>(DatabaseResult<T> result, string what)
		{
			string line = $"{what}: [{result.ErrorCode}] {result.ErrorMessage}";
			return result.ErrorCode == DatabaseErrorCodes.StaleState
				? Log.Debug("InteractableSystem", line)
				: Log.Warning("InteractableSystem", line);
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
			if (!claim.IsSuccess)
			{
				await Log.Warning("InteractableSystem", $"Group finder could not claim character {waiter.CharacterID} for instance {run.ID}: [{claim.ErrorCode}] {claim.ErrorMessage}");
				return false;
			}
			if (!claim.Data)
			{
				return false;
			}

			if (await partySystem.TryAddCharacterToPartyAsync(waiter.Connection, waiter.CharacterID, run.PartyID, waiter.HealthPCT) != PartyJoinOutcome.Joined)
			{
				/* A release that fails leaves the row matched to a run whose party refused them.
				 * The next pump reads it back as a match with the membership beside it, and
				 * GroupFinderRules.IsMatchHonoured — which checks the membership a match names
				 * before moving anybody — takes them out of the queue instead of moving them into
				 * somebody else's run as a stranger. */
				DatabaseResult<bool> release = await queueService.ReleaseClaimAsync(waiter.CharacterID, run.ID);
				if (!release.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Group finder could not release character {waiter.CharacterID}'s claim on instance {run.ID} after the party refused them; the next pump takes them out of the queue: [{release.ErrorCode}] {release.ErrorMessage}");
				}
				else if (!release.Data)
				{
					await Log.Warning("InteractableSystem",
						$"Group finder found no claim of character {waiter.CharacterID}'s on instance {run.ID} to release after the party refused them.");
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
		/// <para>
		/// For a match this server has just made by a route that does not know the rank: the
		/// late-join, where the party system may have repaired the party's leadership onto the
		/// joiner. The pump's own read-back carries the membership with each matched row, and a
		/// group the pump has just formed knows the ranks it wrote, so neither comes here.
		/// </para>
		/// <para>
		/// The rank is read rather than assumed because it is the party's to change: what they
		/// are told on the way in should be what is true. The same read decides whether the match
		/// is honoured at all (<see cref="GroupFinderRules.IsMatchHonoured"/>). A read that fails
		/// decides nothing either way: the row stays matched and the next pump's read-back
		/// carries the membership.
		/// </para>
		/// </remarks>
		private async Task DispatchMatchedAsync(long characterID, long partyID, long instanceID)
		{
			if (partyID <= 0)
			{
				DispatchMatched(characterID, partyID, instanceID, PartyRank.Member);
				return;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
			{
				return;
			}

			DatabaseResult<CharacterPartyData?> membership = await charPartyService.FetchAsync(characterID);
			if (!membership.IsSuccess)
			{
				await Log.Warning("InteractableSystem", $"Group finder could not read character {characterID}'s party membership before moving them; retrying next pump: [{membership.ErrorCode}] {membership.ErrorMessage}");
				return;
			}

			long memberPartyID = membership.Data.HasValue ? membership.Data.Value.PartyID : 0;
			if (GroupFinderRules.IsMatchHonoured(partyID, memberPartyID))
			{
				DispatchMatched(characterID, partyID, instanceID, (PartyRank)membership.Data.Value.Rank);
				return;
			}

			await RemoveUnhonouredMatchAsync(characterID, partyID, instanceID);
		}

		/// <summary>
		/// Hands a matched character whose rank is known to the main thread for the transfer.
		/// </summary>
		private void DispatchMatched(long characterID, long partyID, long instanceID, PartyRank rank)
		{
			TryEnqueueMainThread(() => HandleMatchedEntry(characterID, partyID, instanceID, rank));
		}

		/// <summary>
		/// Takes a character whose row is matched into a party they are not in out of the queue,
		/// and tells them.
		/// </summary>
		/// <remarks>
		/// A late-join whose claim could not be released after the party refused them, or a member
		/// the group has since dropped. Moving them would put a stranger in somebody else's run,
		/// with no leader able to remove them.
		/// </remarks>
		private async Task RemoveUnhonouredMatchAsync(long characterID, long partyID, long instanceID)
		{
			await Log.Warning("InteractableSystem",
				$"Group finder: character {characterID}'s row is matched into party {partyID} (instance {instanceID}), which they are not in; taking them out of the queue instead of moving them.");
			await DeleteGroupFinderRowAsync(characterID);
			TryEnqueueMainThread(() =>
			{
				if (groupFinderEntries.TryGetValue(characterID, out GroupFinderEntry entry))
				{
					NetworkConnection conn = entry.Connection;
					ForgetGroupFinderEntry(characterID);
					SendGroupFinderRefusal(conn, GroupFinderRefusalReason.Removed, entry.Kind);
				}
			});
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

			double now = MonotonicClock.NowSeconds;

			if (entry.State != GroupFinderState.Matched)
			{
				entry.State = GroupFinderState.Matched;
				entry.MatchedAt = now;
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
				EnqueuePersistence(() => DeleteGroupFinderRowAsync(characterID), characterID);
				return;
			}

			/* At the door, as well as free to travel. A matched player who stepped outside the
			 * leash is not moved until they step back in — the transfer must never surprise
			 * somebody who has walked off — and the grace bounds how long the group waits. */
			bool canTransfer = !character.IsInInstance() &&
				CharacterStateValidation.CanActOrMove(character) &&
				IsNearEntrance(entry);

			switch (GroupFinderRules.ResolveMatchedTransfer(canTransfer, now - entry.MatchedAt, groupFinderTransferGraceSeconds))
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
			if (partyID > 0 && character.TryGet(out IPartyController partyController) && partyController.ID != partyID)
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
					Member = new PartyAddEntry()
					{
						CharacterID = characterID,
						Rank = rank,
						HealthPCT = PartyVitalsQuantiser.FractionToByte(healthPCT),
					},
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
			EnqueuePersistence(() => DeleteGroupFinderRowAsync(characterID), characterID);

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

			EnqueuePersistence(async () =>
			{
				try
				{
					/* Whether they actually left decides what they are told about the party. The
					 * controller used to be cleared and PartyLeaveBroadcast sent whatever the removal
					 * did, so a removal the party system refused — a mutation already in flight, or a
					 * membership row it could not read or delete — left them in the party in the
					 * database and out of it on their screen. Refused, they stay a member: the group
					 * is real, and the entrance will take them to its run. */
					bool removed = true;
					if (partyID > 0 && Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
					{
						removed = await partySystem.RemoveCharacterFromPartyAsync(characterID, partyID, "matched by the group finder but never became free to travel");
						if (!removed)
						{
							await Log.Warning("InteractableSystem", $"Group finder: character {characterID} could not be removed from party {partyID} after missing its transfer; they remain a member.");
						}
					}

					/* An arena seat is left for the match coordinator: the match's gathering timeout
					 * drops a seat that never arrives, so nothing else has to. */
					await DeleteGroupFinderRowAsync(characterID);

					TryEnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive || conn.FirstObject == null)
						{
							return;
						}

						IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
						if (removed && partyID > 0 && partyController != null && partyController.ID == partyID)
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
		private void SendGroupFinderRefusal(NetworkConnection conn, GroupFinderRefusalReason reason, SceneType kind = SceneType.Group)
		{
			if (conn == null || !conn.IsActive || Server?.NetworkWrapper == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new GroupFinderStatusBroadcast()
			{
				State = GroupFinderState.None,
				Kind = kind,
				Reason = reason,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>
		/// The shared scene type as the database service takes it. The two enums share values,
		/// not names.
		/// </summary>
		private static FishMMO.Database.Data.Enums.SceneType DbSceneType(SceneType kind)
		{
			return (FishMMO.Database.Data.Enums.SceneType)(int)kind;
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
				Kind = entry.Kind,
				DungeonTemplateID = entry.DungeonTemplateID,
				ArenaTemplateID = entry.ArenaTemplateID,
				Difficulty = entry.Difficulty,
				WaitingCount = waitingCount,
				GroupSize = entry.GroupSize,
				Reason = reason,
			}, true, FishNet.Transporting.Channel.Reliable);
		}
	}
}
