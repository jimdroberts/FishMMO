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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneType = FishMMO.Shared.SceneType;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Arena match coordinator: runs every arena match hosted on this scene server from the first
	/// arrival to the instance closing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Ownership follows the instance.</b> The scene server that hosts an arena's instance runs
	/// its match, whether or not it formed it. It learns of the match when the first seated player
	/// spawns into the instance: the instance row says it is PvP, the match row is read by
	/// instance id, and from then on this coordinator owns the state machine below. Nothing about
	/// a match is shared between servers while it runs; only its result is written back.
	/// </para>
	/// <para>
	/// <b>Phases.</b> <em>Gathering</em> waits for every seat to arrive, up to the template's
	/// timeout, after which absentees are dropped and the match goes on if two teams still have
	/// players, or is cancelled. <em>ReadyCheck</em> asks every present player to accept; a decline
	/// or a silence cancels the match and locks the culprit out of the queue for a while.
	/// <em>Countdown</em> moves everyone to their team's spawn and counts down the template's
	/// seconds, broadcasting each one so clients can fire their cues; nobody can be hurt yet,
	/// because <see cref="ArenaTeamRegistry"/> reports every seat as an ally until the match is
	/// live. <em>Live</em> scores kills and objectives, respawns the dead at their team's spawn,
	/// fills seats vacated early from the queue while the backfill window is open, gives a
	/// disconnected player a grace to come back, and ends on the score limit, the clock, or a
	/// walkover. <em>Ended</em> writes the tallies, the result and the ratings, adjusts every
	/// present player's PvP attributes, raises the reward hook, shows the results screen for the
	/// template's seconds, and closes the instance through the same path a dungeon closes by.
	/// </para>
	/// <para>
	/// <b>Leaving.</b> A player who leaves a live match forfeits: their loss is written to their
	/// attributes at once, while they are still in memory, and they are locked out of the queue.
	/// If the template grants a reconnect grace, their seat is held for that long; a player who
	/// comes back inside it is refunded the loss and unlocked, and plays on. Only when the grace
	/// runs out is the seat vacated for backfill.
	/// </para>
	/// <para>
	/// <b>Rewards</b> are not decided here. At the end of a match the coordinator raises
	/// <see cref="ArenaServerEvents.OnMatchEnded"/> with everything a reward system needs and runs
	/// the template's reward trigger lists; both are empty until such a system arrives.
	/// </para>
	/// </remarks>
	public partial class InteractableSystem
	{
		/// <summary>Ingress-guard operation code for the ready-check answer. Unique among this system's operations.</summary>
		private const byte ArenaReadyOperation = 16;

		/// <summary>Minimum milliseconds between ready-check answers from one connection.</summary>
		private const int ArenaReadyDebounceMilliseconds = 250;

		/// <summary>Seconds between arena ticks. One, because the countdown is announced per second.</summary>
		private const float ArenaTickSeconds = 1.0f;

		/// <summary>How long a cancelled match's occupants see the notice before being returned.</summary>
		private const int ArenaCancelledSeconds = 5;

		/// <summary>Seconds between the tick's retries of a failed arena read: the match, its seats, its ratings.</summary>
		private const double ArenaReadRetrySeconds = 5.0;

		/// <summary>Tick retries of one failed arena read before it is given up on and logged as an error.</summary>
		private const int ArenaReadMaxRetries = 6;

		/// <summary>Attempts at one idempotent arena write before its failure is logged and left.</summary>
		private const int ArenaWriteAttempts = 3;

		/// <summary>Backoff step between attempts at an arena write, multiplied by the attempt number.</summary>
		private const int ArenaWriteRetryDelayMilliseconds = 500;

		/// <summary>Attribute template names the arena adjusts. Authored as CharacterAttributeTemplate assets.</summary>
		private const string PvPRankAttributeName = "PvP Rank";
		private const string PvPWinsAttributeName = "PvP Wins";
		private const string PvPLossesAttributeName = "PvP Losses";
		private const string PvPMatchesAttributeName = "PvP Matches";

		/// <summary>One seat as this server tracks it.</summary>
		private sealed class ArenaSeatState
		{
			public long CharacterID;
			/// <summary>Last known name, so the feed can name a player who has already left.</summary>
			public string Name;
			public int Team;
			public int Kills;
			public int Deaths;
			public int Score;
			/// <summary>Kills since last death.</summary>
			public int Streak;
			/// <summary>Standing in the instance right now.</summary>
			public bool Present;
			/// <summary>
			/// The player's connection while <see cref="Present"/>, or null. Written only by
			/// <see cref="SyncSeatConnection"/>, which keeps the team's connection set in step.
			/// </summary>
			public NetworkConnection Connection;
			/// <summary>Never arrived before the gathering timeout; no longer part of the match.</summary>
			public bool Dropped;
			/// <summary>When a dead player is put back, or null while alive.</summary>
			public double? RespawnAt;
			/// <summary>Left the match for good while it was live. Their seat is vacated; nothing more is written for them but a rating loss.</summary>
			public bool Forfeited;
			/// <summary>Disconnected from a live match and inside the reconnect grace, or null.</summary>
			public double? DisconnectedAt;
			/// <summary>Rank change written when they disconnected, so a return inside the grace can refund it exactly.</summary>
			public int ForfeitRankDelta;
			/// <summary>Whether the win/loss/matches attributes were charged for a forfeit.</summary>
			public bool ForfeitCharged;
			/// <summary>Scene object id of the enemy flag stand whose flag they carry, or 0.</summary>
			public long CarriedFlagObjectiveID;
			/// <summary>Ready check: answered.</summary>
			public bool Answered;
			/// <summary>Ready check: accepted.</summary>
			public bool Ready;
			/// <summary>Took a vacated seat from the queue after the match went live.</summary>
			public bool Backfilled;
			/// <summary>Ranked: rating and games before the match.</summary>
			public int RatingBefore = ArenaRating.DefaultRating;
			public int GamesBefore;
			/// <summary>
			/// Ranked: whether <see cref="RatingBefore"/> and <see cref="GamesBefore"/> came from a
			/// read that succeeded. Only such a seat is rated at the end — see <see cref="LoadArenaRatings"/>.
			/// </summary>
			public bool RatingLoaded;
			/// <summary>Ranked: written at the end.</summary>
			public int RatingDelta;
			public int NewRating;
			/// <summary>Where they last stood, for dropping a flag after they are gone.</summary>
			public Vector3 LastPosition;
		}

		/// <summary>One flag stand or control point as this server tracks it.</summary>
		private sealed class ArenaObjectiveState
		{
			public long ObjectiveID;
			public ArenaObjectiveKind Kind;
			/// <summary>Flag stand: the flag's team. Control point: owner, or -1.</summary>
			public int Team = -1;
			/// <summary>Flag stand: where the flag is.</summary>
			public ArenaFlagState Flag = ArenaFlagState.Home;
			/// <summary>Flag stand: who carries it, or 0.</summary>
			public long CarrierCharacterID;
			/// <summary>Flag stand: where a dropped flag lies.</summary>
			public Vector3 DropPosition;
			/// <summary>Flag stand: when a dropped flag goes home by itself.</summary>
			public double DropExpiresAt;
			/// <summary>Control point: team whose capture is in progress, or -1.</summary>
			public int ProgressTeam = -1;
			/// <summary>Control point: interactions towards a capture.</summary>
			public int Progress;
			/// <summary>Control point: real seconds held and not yet scored.</summary>
			public double HeldSeconds;
			/// <summary>
			/// Control point: the moment held time has been accrued up to, in
			/// <see cref="MonotonicClock"/> seconds. Reset on capture; not a number until the first.
			/// </summary>
			public double HeldAccruedTo = double.NaN;
		}

		/// <summary>One match hosted here.</summary>
		private sealed class ArenaMatchState
		{
			public long MatchID;
			public long InstanceID;
			/// <summary>The instance's scene, for scene-wide sends. Its handle is <see cref="SceneHandle"/>.</summary>
			public Scene Scene;
			public int SceneHandle;
			public string SceneName;
			public ArenaTemplate Template;
			public int Format;
			public int TeamCount;
			public int TeamSize;
			public ArenaMatchPhase Phase;
			public double PhaseEndsAt;
			public int LastBroadcastSecond = -1;
			/// <summary>Live: the clock second the time warnings were last checked at.</summary>
			public int LastWarningCheckSecond = int.MaxValue;
			public int[] TeamScores;
			public int WinnerTeam = -1;
			public bool Ranked;
			public long SeasonID;
			/// <summary>Ranked: tick retries spent on seats whose rating read failed, and when the next may run.</summary>
			public int RatingReadRetries;
			public double NextRatingReadAt;
			/// <summary>Whether the first kill has happened.</summary>
			public bool FirstBloodDone;
			/// <summary>Per team: whether the near-limit announcement fired.</summary>
			public bool[] NearLimitFired;
			/// <summary>Time warnings already fired.</summary>
			public readonly HashSet<int> TimeWarningsFired = new HashSet<int>();
			/// <summary>
			/// 1 while a seat re-read is in flight, 0 otherwise. One at a time.
			/// </summary>
			/// <remarks>
			/// An int for <see cref="Interlocked"/>, set here and cleared by the read's worker in its
			/// own <c>finally</c>, as the group finder's pump flag is. It used to be cleared by an
			/// action on the bounded main-thread queue; a queue that refused that action left it set
			/// for the rest of the match, and no backfilled player arriving after was ever seated.
			/// </remarks>
			public int SeatReloadInFlight;
			/// <summary>
			/// A stranger arrived and the seats should be re-read; set while a read is in flight, the
			/// tick starts one once it lands. A backfill who arrived during a read that started
			/// before their seat was written would otherwise have waited for the next stranger.
			/// </summary>
			public bool SeatReloadWanted;
			/// <summary>A seat re-read failed and is owed a retry by the tick; how many have run, and when the next may.</summary>
			public bool SeatReloadDue;
			public int SeatReloadRetries;
			public double NextSeatReloadAt;
			public readonly Dictionary<long, ArenaSeatState> Seats = new Dictionary<long, ArenaSeatState>();
			/// <summary>
			/// Per team, the connections of its seats that are on the team channel
			/// (<see cref="ArenaRules.IsOnTeamChannel"/>). Kept by <see cref="SyncSeatConnection"/>.
			/// </summary>
			public HashSet<NetworkConnection>[] TeamConnections;
			/// <summary>Objectives in the scene, by scene object id. Empty for deathmatch.</summary>
			public readonly Dictionary<long, ArenaObjectiveState> Objectives = new Dictionary<long, ArenaObjectiveState>();

			public ArenaMode Mode => Template != null ? Template.Mode : ArenaMode.TeamDeathmatch;
		}

		/// <summary>Matches hosted here, by instance row id. Main thread only.</summary>
		private readonly Dictionary<long, ArenaMatchState> arenaMatchesByInstance = new Dictionary<long, ArenaMatchState>();

		/// <summary>Instance row id by Unity scene handle, for the events that only know the scene.</summary>
		private readonly Dictionary<int, long> arenaInstanceBySceneHandle = new Dictionary<int, long>();

		/// <summary>Instances whose match rows are being read, as a set.</summary>
		/// <remarks>
		/// Added on the main thread when a read is issued and removed by that read's worker in its
		/// own <c>finally</c> — hence concurrent. It used to be removed by an action on the bounded
		/// main-thread queue, and a queue that refused the action left the instance marked as
		/// loading for good: its match was never hosted.
		/// </remarks>
		private readonly ConcurrentDictionary<long, byte> arenaMatchesLoading = new ConcurrentDictionary<long, byte>();

		/// <summary>A match read that failed on the database and is owed a retry by the tick.</summary>
		private struct ArenaMatchLoadRetry
		{
			public Scene Scene;
			public string SceneName;
			public int Retries;
			public double NextAt;
		}

		/// <summary>Failed match reads awaiting their retry, by instance row id. Main thread only.</summary>
		private readonly Dictionary<long, ArenaMatchLoadRetry> arenaMatchLoadRetries = new Dictionary<long, ArenaMatchLoadRetry>();

		/// <summary>Attribute templates by name, resolved once from the cache.</summary>
		private readonly Dictionary<string, CharacterAttributeTemplate> pvpAttributeTemplates = new Dictionary<string, CharacterAttributeTemplate>(StringComparer.Ordinal);

		/// <summary>Whether the missing-attribute warning has been logged, so it is logged once.</summary>
		private readonly HashSet<string> pvpAttributeWarnings = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>Subscribes to the events a match is driven by.</summary>
		private void InitializeArenaMatches()
		{
			arenaMatchesByInstance.Clear();
			arenaInstanceBySceneHandle.Clear();
			arenaMatchesLoading.Clear();
			arenaMatchLoadRetries.Clear();
			pvpAttributeTemplates.Clear();
			pvpAttributeWarnings.Clear();
			ArenaTeamRegistry.Clear();

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) && characterSystem != null)
			{
				characterSystem.OnSpawnCharacter += CharacterSystem_OnArenaCharacterSpawned;
				characterSystem.OnDespawnCharacter += CharacterSystem_OnArenaCharacterDespawned;
				/* OnDisconnect fires for every way out of an instance — leave, kick, transfer, quit
				 * — while the character is still in memory and BEFORE it is saved, so a loss
				 * recorded here on their attributes reaches the database with them. */
				characterSystem.OnDisconnect += CharacterSystem_OnArenaCharacterLeft;
			}
			else
			{
				Log.Warning("InteractableSystem", "Arena: ICharacterSystem not found; matches cannot see players arrive.");
			}

			/* Static events, and this ScriptableObject can survive a domain reload in the editor:
			 * removed before added so a stale subscription is never doubled. */
			ICharacterDamageController.OnKilled -= CharacterDamageController_OnArenaKilled;
			ICharacterDamageController.OnKilled += CharacterDamageController_OnArenaKilled;
			IArenaObjective.OnServerInteracted -= ArenaObjective_OnServerInteracted;
			IArenaObjective.OnServerInteracted += ArenaObjective_OnServerInteracted;

			Server.NetworkWrapper.RegisterBroadcast<ArenaReadyResponseBroadcast>(OnServerArenaReadyResponseReceived, true);

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(ArenaTickSeconds, OnArenaTick);
			}
		}

		/// <summary>Unsubscribes and forgets every match. The instances outlive this only until their own sweep.</summary>
		private void DeinitializeArenaMatches()
		{
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) && characterSystem != null)
			{
				characterSystem.OnSpawnCharacter -= CharacterSystem_OnArenaCharacterSpawned;
				characterSystem.OnDespawnCharacter -= CharacterSystem_OnArenaCharacterDespawned;
				characterSystem.OnDisconnect -= CharacterSystem_OnArenaCharacterLeft;
			}

			ICharacterDamageController.OnKilled -= CharacterDamageController_OnArenaKilled;
			IArenaObjective.OnServerInteracted -= ArenaObjective_OnServerInteracted;

			Server.NetworkWrapper.UnregisterBroadcast<ArenaReadyResponseBroadcast>(OnServerArenaReadyResponseReceived);

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnArenaTick);
			}

			arenaMatchesByInstance.Clear();
			arenaInstanceBySceneHandle.Clear();
			arenaMatchesLoading.Clear();
			arenaMatchLoadRetries.Clear();
			ArenaTeamRegistry.Clear();
		}

		// ──────────────────────────────────────────────────────────────────
		//  Arrivals and departures
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// A character spawned into a scene. If it is an arena instance, this server now hosts its
		/// match: read it if this is the first arrival, and seat the player.
		/// </summary>
		private void CharacterSystem_OnArenaCharacterSpawned(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			if (character == null || !scene.IsValid() ||
				!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) ||
				!mappingData.SceneInstanceByHandle.TryGetValue(scene.handle, out ISceneInstanceDetails details) ||
				details.SceneType != SceneType.PvP)
			{
				return;
			}

			if (arenaMatchesByInstance.TryGetValue(details.SceneID, out ArenaMatchState state))
			{
				SeatArrived(state, character);
				return;
			}

			if (!arenaMatchesLoading.TryAdd(details.SceneID, 0))
			{
				// Already being read; the arrival is picked up from the scene when it lands.
				return;
			}

			long instanceID = details.SceneID;
			string sceneName = details.Name;

			if (!TryEnqueueAsyncWork(() => LoadArenaMatchAsync(instanceID, scene, sceneName), instanceID))
			{
				arenaMatchesLoading.TryRemove(instanceID, out _);
				Log.Warning("InteractableSystem", $"Arena: could not enqueue the match read for instance {instanceID}.");
			}
		}

		/// <summary>Reads the match and its seats for an instance this server has just started hosting.</summary>
		/// <remarks>
		/// Owns the instance's loading mark from the moment it was issued and clears it in its own
		/// <c>finally</c>, whatever happens to the outcome it hands the main thread. A second read
		/// issued in the moment between that and the outcome landing is harmless:
		/// <see cref="RegisterArenaMatch"/> hosts a match once.
		/// </remarks>
		private async Task LoadArenaMatchAsync(long instanceID, Scene scene, string sceneName)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
				{
					return;
				}

				/* A read that failed is not a read that found nothing.
				 *
				 * The two used to share one branch, so a transient error on the arrival that happened
				 * to trigger the read was logged as "no match row" and the match was never hosted. The
				 * next arrival would read again — but when the failure landed on the LAST arrival there
				 * was no next one, and everyone stood in an arena with no coordinator until they left.
				 * A failure is now owed a retry by the tick; only a successful read that finds no row
				 * makes this a plain instance. */
				DatabaseResult<ArenaMatchData?> matchResult = await matchService.FetchByInstanceAsync(instanceID);
				if (!matchResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"Arena: could not read the match of instance {instanceID} ('{sceneName}'); retrying: [{matchResult.ErrorCode}] {matchResult.ErrorMessage}");
					TryEnqueueMainThread(() => OnArenaMatchLoadFailed(instanceID, scene, sceneName));
					return;
				}
				if (!matchResult.Data.HasValue)
				{
					await Log.Warning("InteractableSystem", $"Arena: instance {instanceID} ('{sceneName}') is a PvP scene with no match row; it will run as a plain instance.");
					TryEnqueueMainThread(() => arenaMatchLoadRetries.Remove(instanceID));
					return;
				}

				ArenaMatchData match = matchResult.Data.Value;
				DatabaseResult<IReadOnlyList<ArenaMatchMemberData>> membersResult = await matchService.FetchMembersAsync(match.ID);
				if (!membersResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"Arena: could not read the seats of match {match.ID}; retrying: [{membersResult.ErrorCode}] {membersResult.ErrorMessage}");
					TryEnqueueMainThread(() => OnArenaMatchLoadFailed(instanceID, scene, sceneName));
					return;
				}

				IReadOnlyList<ArenaMatchMemberData> members = membersResult.Data;
				TryEnqueueMainThread(() => RegisterArenaMatch(match, members, scene));
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error reading arena match for instance {instanceID}: {ex}");
				TryEnqueueMainThread(() => OnArenaMatchLoadFailed(instanceID, scene, sceneName));
			}
			finally
			{
				arenaMatchesLoading.TryRemove(instanceID, out _);
			}
		}

		/// <summary>
		/// Schedules the tick's retry of a match read that failed. Main thread only.
		/// </summary>
		/// <remarks>
		/// Bounded by <see cref="ArenaReadMaxRetries"/>, counted where the retries are issued. Past
		/// that the instance is left to the arrival path, which still reads again whenever somebody
		/// else spawns into it.
		/// </remarks>
		private void OnArenaMatchLoadFailed(long instanceID, Scene scene, string sceneName)
		{
			if (arenaMatchesByInstance.ContainsKey(instanceID))
			{
				arenaMatchLoadRetries.Remove(instanceID);
				return;
			}

			arenaMatchLoadRetries.TryGetValue(instanceID, out ArenaMatchLoadRetry retry);
			retry.Scene = scene;
			retry.SceneName = sceneName;
			retry.NextAt = MonotonicClock.NowSeconds + ArenaReadRetrySeconds;
			arenaMatchLoadRetries[instanceID] = retry;
		}

		/// <summary>Re-issues the match reads that failed, once their retry is due. Main thread only.</summary>
		/// <remarks>
		/// A retry is rescheduled when it is issued, not only when its failure is reported: a report
		/// the main-thread queue refused would otherwise have left it waiting forever. The loading
		/// mark keeps a retry from overlapping a read still in flight.
		/// </remarks>
		private void RetryArenaMatchLoads(double now)
		{
			if (arenaMatchLoadRetries.Count == 0)
			{
				return;
			}

			foreach (long instanceID in arenaMatchLoadRetries.Keys.ToList())
			{
				ArenaMatchLoadRetry retry = arenaMatchLoadRetries[instanceID];

				// Hosted by an arrival's read in the meantime, or the instance is gone: nothing owed.
				if (arenaMatchesByInstance.ContainsKey(instanceID) ||
					!IsArenaInstanceLoaded(retry.Scene.handle, instanceID))
				{
					arenaMatchLoadRetries.Remove(instanceID);
					continue;
				}

				if (now < retry.NextAt || arenaMatchesLoading.ContainsKey(instanceID))
				{
					continue;
				}

				if (retry.Retries >= ArenaReadMaxRetries)
				{
					arenaMatchLoadRetries.Remove(instanceID);
					Log.Error("InteractableSystem", $"Arena: gave up reading the match of instance {instanceID} ('{retry.SceneName}') after {retry.Retries} retries; it is not hosted until somebody else arrives.");
					continue;
				}

				if (!arenaMatchesLoading.TryAdd(instanceID, 0))
				{
					continue;
				}

				++retry.Retries;
				retry.NextAt = now + ArenaReadRetrySeconds;
				arenaMatchLoadRetries[instanceID] = retry;

				Scene scene = retry.Scene;
				string sceneName = retry.SceneName;
				if (!TryEnqueueAsyncWork(() => LoadArenaMatchAsync(instanceID, scene, sceneName), instanceID))
				{
					// Still scheduled: the next retry is already set for its time.
					arenaMatchesLoading.TryRemove(instanceID, out _);
				}
			}
		}

		/// <summary>Creates the local match state and seats everyone already standing in the scene. Main thread only.</summary>
		private void RegisterArenaMatch(ArenaMatchData match, IReadOnlyList<ArenaMatchMemberData> members, Scene scene)
		{
			arenaMatchLoadRetries.Remove(match.InstanceID);
			int sceneHandle = scene.handle;

			if (arenaMatchesByInstance.ContainsKey(match.InstanceID))
			{
				return;
			}

			ArenaTemplate template = match.TemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(match.TemplateID) : null;
			if (template == null)
			{
				Log.Warning("InteractableSystem", $"Arena: match {match.ID} names template {match.TemplateID}, which this server cannot resolve; cancelling it.");
			}

			var state = new ArenaMatchState
			{
				MatchID = match.ID,
				InstanceID = match.InstanceID,
				Scene = scene,
				SceneHandle = sceneHandle,
				SceneName = match.SceneName,
				Template = template,
				Format = match.Format,
				TeamCount = Math.Max(2, match.TeamCount),
				TeamSize = Math.Max(1, match.TeamSize),
				Phase = ArenaMatchPhase.Gathering,
				PhaseEndsAt = MonotonicClock.NowSeconds + (template != null ? template.GatheringTimeoutSeconds : 90),
				Ranked = match.Ranked || (template != null && template.IsRankedFormat(match.Format)),
				SeasonID = match.SeasonID,
			};
			state.TeamScores = new int[state.TeamCount];
			state.NearLimitFired = new bool[state.TeamCount];
			state.TeamConnections = new HashSet<NetworkConnection>[state.TeamCount];
			for (int t = 0; t < state.TeamCount; ++t)
			{
				state.TeamConnections[t] = new HashSet<NetworkConnection>();
			}

			foreach (ArenaMatchMemberData member in members)
			{
				if (member.Status != (int)ArenaSeatStatus.Seated)
				{
					continue;
				}
				state.Seats[member.CharacterID] = new ArenaSeatState
				{
					CharacterID = member.CharacterID,
					Team = Mathf.Clamp(member.Team, 0, state.TeamCount - 1),
					Kills = member.Kills,
					Deaths = member.Deaths,
					Score = member.Score,
				};
			}

			arenaMatchesByInstance[match.InstanceID] = state;
			arenaInstanceBySceneHandle[sceneHandle] = match.InstanceID;
			PublishArenaRoster(state);
			DiscoverArenaObjectives(state);

			if (state.Ranked)
			{
				LoadArenaRatings(state, state.Seats.Keys.ToList());
			}

			/* Whoever arrived while the rows were being read: the scene's own connections, rather
			 * than every character on the server asked for its scene one native call at a time.
			 * Copied first, because seating a resident sends, and FishNet's set is not ours to
			 * enumerate across anything that might change it. */
			if (Server.NetworkWrapper.TryGetSceneConnections(scene, out HashSet<NetworkConnection> sceneConnections) &&
				Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				var residents = new List<IPlayerCharacter>(sceneConnections.Count);
				foreach (NetworkConnection resident in sceneConnections)
				{
					if (resident != null && charMapping.ConnectionCharacters.TryGetValue(resident, out IPlayerCharacter character) && character != null)
					{
						residents.Add(character);
					}
				}
				foreach (IPlayerCharacter character in residents)
				{
					SeatArrived(state, character);
				}
			}

			if (template == null)
			{
				CancelArenaMatch(state, "its arena template could not be resolved");
				return;
			}

			Log.Debug("InteractableSystem", $"Arena: hosting match {state.MatchID} ('{template.name}' {template.GetFormatName(state.Format)}{(state.Ranked ? ", ranked" : "")}) in instance {state.InstanceID}; {state.Seats.Count} seats.");
			BroadcastArenaState(state);
		}

		/// <summary>
		/// Reads the season ratings of the given seats, stamping the match ranked in its season if the
		/// forming server did not get to it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A failed read leaves the seats unread; it never falls back to the default.</b> It used
		/// to: every seat was given the default rating and no games, and the match was marked as
		/// read. The end-of-match write replaces the stored rating outright rather than applying a
		/// change to it, so one transient error here reset every player in the match to the default
		/// plus or minus a placement-sized swing. The default is still right for a seat the read
		/// SUCCEEDED for and found no row — that is a first ranked game — and only such a seat is
		/// marked <see cref="ArenaSeatState.RatingLoaded"/>.
		/// </para>
		/// <para>
		/// A seat left unread is retried by the tick (<see cref="TickArenaReads"/>), and one still
		/// unread when the match ends is not rated at all.
		/// </para>
		/// </remarks>
		private void LoadArenaRatings(ArenaMatchState state, List<long> characterIDs)
		{
			long matchID = state.MatchID;
			long instanceID = state.InstanceID;
			long seasonID = state.SeasonID;

			// Gives this read the retry interval to land before the tick tries again.
			state.NextRatingReadAt = MonotonicClock.NowSeconds + ArenaReadRetrySeconds;

			EnqueuePersistence(async () =>
			{
				long resolvedSeason = seasonID;
				Dictionary<long, (int rating, int games)> ratings = null;
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IArenaRatingService>(out var ratingService) ||
						!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
					{
						await Log.Warning("InteractableSystem", $"Arena: match {matchID} is ranked but the rating services are unavailable; its seats stay unrated.");
						return;
					}

					if (resolvedSeason <= 0)
					{
						DatabaseResult<ArenaSeasonData> seasonResult = await ratingService.GetOrCreateActiveSeasonAsync();
						if (!seasonResult.IsSuccess)
						{
							await Log.Warning("InteractableSystem", $"Arena: match {matchID} is ranked but no season could be resolved: [{seasonResult.ErrorCode}] {seasonResult.ErrorMessage}");
							return;
						}
						resolvedSeason = seasonResult.Data.ID;

						/* The stamp is what the history and any other reader go by; the ratings
						 * themselves only need the season this server now holds. */
						DatabaseResult<bool> stamp = await WriteArenaRowAsync(() => matchService.SetRankedAsync(matchID, resolvedSeason));
						if (!stamp.IsSuccess)
						{
							await Log.Warning("InteractableSystem", $"Arena: match {matchID} could not be stamped ranked in season {resolvedSeason}: [{stamp.ErrorCode}] {stamp.ErrorMessage}");
						}
					}

					DatabaseResult<IReadOnlyList<ArenaRatingData>> ratingsResult = await ratingService.FetchRatingsAsync(resolvedSeason, characterIDs);
					if (!ratingsResult.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"Arena: could not read the ratings of {characterIDs.Count} seats in match {matchID}; they stay unrated until a retry lands: [{ratingsResult.ErrorCode}] {ratingsResult.ErrorMessage}");
						return;
					}

					ratings = new Dictionary<long, (int rating, int games)>();
					foreach (ArenaRatingData r in ratingsResult.Data)
					{
						ratings[r.CharacterID] = (r.Rating, r.Games);
					}
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error reading ratings for arena match {matchID}: {ex}");
				}
				finally
				{
					/* Handed back on every route, failures included, so a season resolved before the
					 * rating read failed is kept and the retry does not create or stamp it again. */
					long season = resolvedSeason;
					Dictionary<long, (int rating, int games)> loaded = ratings;
					TryEnqueueMainThread(() => ApplyArenaRatings(instanceID, matchID, characterIDs, season, loaded));
				}
			}, matchID);
		}

		/// <summary>
		/// Installs a rating read on the seats it covered. Main thread only.
		/// </summary>
		/// <param name="ratings">The rows read, or null when the read failed.</param>
		private void ApplyArenaRatings(long instanceID, long matchID, List<long> characterIDs, long seasonID, Dictionary<long, (int rating, int games)> ratings)
		{
			if (!arenaMatchesByInstance.TryGetValue(instanceID, out ArenaMatchState live) || live.MatchID != matchID)
			{
				return;
			}

			if (seasonID > 0)
			{
				live.SeasonID = seasonID;
			}

			if (ratings == null)
			{
				return;
			}

			foreach (long id in characterIDs)
			{
				if (!live.Seats.TryGetValue(id, out ArenaSeatState seat))
				{
					continue;
				}

				// No row after a read that succeeded is a first ranked game: the default is the truth.
				if (ratings.TryGetValue(id, out var r))
				{
					seat.RatingBefore = r.rating;
					seat.GamesBefore = r.games;
				}
				else
				{
					seat.RatingBefore = ArenaRating.DefaultRating;
					seat.GamesBefore = 0;
				}
				seat.RatingLoaded = true;
			}
		}

		/// <summary>Marks a seat present. A stranger with no seat may be a backfill; the seats are re-read to find out.</summary>
		private void SeatArrived(ArenaMatchState state, IPlayerCharacter character)
		{
			if (!state.Seats.TryGetValue(character.ID, out ArenaSeatState seat))
			{
				/* Any stranger while live, not only inside the backfill window. The window bounds
				 * when the QUEUE may seat somebody; the seated player then has to be moved here,
				 * which takes seconds, so a backfill taken near the window's end used to arrive
				 * after it had closed here, was never re-read, and stood in the match with no team.
				 * The other strangers are spectating game masters, and a re-read is cheap. */
				if (state.Phase == ArenaMatchPhase.Live)
				{
					state.SeatReloadWanted = true;
					TryStartSeatReload(state);
					return;
				}

				Log.Debug("InteractableSystem", $"Arena: {character.CharacterName} entered match {state.MatchID}'s instance without a seat.");
				return;
			}

			seat.Present = true;
			seat.Name = character.CharacterName;
			seat.LastPosition = character.Transform != null ? character.Transform.position : seat.LastPosition;

			/* Arriving during the countdown after having been dropped: reseat them. Arriving once
			 * live stays dropped — the sides were settled when play began. */
			if (seat.Dropped && state.Phase <= ArenaMatchPhase.Countdown)
			{
				seat.Dropped = false;
				PublishArenaRoster(state);
			}
			SyncSeatConnection(state, seat, character.Owner);

			// Back inside the reconnect grace: their seat is theirs, and the forfeit is undone.
			if (seat.DisconnectedAt.HasValue && state.Phase == ArenaMatchPhase.Live && !seat.Forfeited)
			{
				seat.DisconnectedAt = null;
				RefundForfeit(character, seat);
				UnlockArenaQueue(seat.CharacterID);
				BroadcastArenaEvent(state, ArenaEventKind.PlayerReconnected, null, seat, seat.Team, 0);
				Log.Debug("InteractableSystem", $"Arena: {character.CharacterName} reconnected to match {state.MatchID} inside the grace.");
			}

			if (state.Phase == ArenaMatchPhase.Countdown || state.Phase == ArenaMatchPhase.Live)
			{
				MoveToTeamSpawn(state, character, seat.Team);
			}

			if (state.Phase == ArenaMatchPhase.ReadyCheck)
			{
				SendReadyCheck(state, seat, character.Owner);
			}

			BroadcastArenaState(state);
		}

		/// <summary>
		/// Starts a seat re-read if none is in flight. Main thread only.
		/// </summary>
		/// <returns>True when a read was started.</returns>
		private bool TryStartSeatReload(ArenaMatchState state)
		{
			// One at a time. A stranger who arrives meanwhile leaves SeatReloadWanted set, and the tick starts the next.
			if (Interlocked.CompareExchange(ref state.SeatReloadInFlight, 1, 0) != 0)
			{
				return false;
			}

			state.SeatReloadWanted = false;
			long instanceID = state.InstanceID;
			long matchID = state.MatchID;
			if (!TryEnqueueAsyncWork(() => ReloadArenaSeatsAsync(state, instanceID, matchID), matchID))
			{
				Interlocked.Exchange(ref state.SeatReloadInFlight, 0);
				state.SeatReloadWanted = true;
				return false;
			}
			return true;
		}

		/// <summary>
		/// Re-reads a live match's seats after a stranger arrived: a backfill from the queue adds a
		/// row this server has not seen.
		/// </summary>
		/// <param name="flight">
		/// The match whose in-flight mark this read owns. Touched here only through
		/// <see cref="Interlocked"/>, to clear the mark in <c>finally</c>; everything else about the
		/// match is the main thread's.
		/// </param>
		private async Task ReloadArenaSeatsAsync(ArenaMatchState flight, long instanceID, long matchID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
				{
					return;
				}

				/* A failed read is not an empty one. Treated as empty, it seated nobody — and the
				 * backfill it was looking for had already taken a seat in the database and been moved
				 * here, so they stood in the match with no team and no way to score until some other
				 * stranger's arrival happened to read again. The tick retries it instead. */
				DatabaseResult<IReadOnlyList<ArenaMatchMemberData>> membersResult = await matchService.FetchMembersAsync(matchID);
				if (!membersResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"Arena: could not re-read the seats of match {matchID}; retrying: [{membersResult.ErrorCode}] {membersResult.ErrorMessage}");
					TryEnqueueMainThread(() => OnArenaSeatReloadFailed(instanceID, matchID));
					return;
				}
				IReadOnlyList<ArenaMatchMemberData> members = membersResult.Data;

				TryEnqueueMainThread(() =>
				{
					if (!arenaMatchesByInstance.TryGetValue(instanceID, out ArenaMatchState state) || state.MatchID != matchID)
					{
						return;
					}
					state.SeatReloadDue = false;

					var added = new List<long>();
					foreach (ArenaMatchMemberData member in members)
					{
						if (member.Status != (int)ArenaSeatStatus.Seated || state.Seats.ContainsKey(member.CharacterID))
						{
							continue;
						}
						state.Seats[member.CharacterID] = new ArenaSeatState
						{
							CharacterID = member.CharacterID,
							Team = Mathf.Clamp(member.Team, 0, state.TeamCount - 1),
							Backfilled = true,
						};
						added.Add(member.CharacterID);
					}

					if (added.Count == 0)
					{
						return;
					}

					PublishArenaRoster(state);
					if (state.Ranked)
					{
						LoadArenaRatings(state, added);
					}

					if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
					{
						foreach (long id in added)
						{
							if (charMapping.CharactersByID.TryGetValue(id, out IPlayerCharacter character) &&
								character?.GameObject != null && character.GameObject.scene.handle == state.SceneHandle)
							{
								SeatArrived(state, character);
								BroadcastArenaEvent(state, ArenaEventKind.PlayerBackfilled, null, state.Seats[id], state.Seats[id].Team, 0);
								Log.Debug("InteractableSystem", $"Arena: {character.CharacterName} backfilled team {state.Seats[id].Team + 1} in match {state.MatchID}.");
							}
						}
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error re-reading seats of arena match {matchID}: {ex}");
				TryEnqueueMainThread(() => OnArenaSeatReloadFailed(instanceID, matchID));
			}
			finally
			{
				Interlocked.Exchange(ref flight.SeatReloadInFlight, 0);
			}
		}

		/// <summary>Owes a failed seat re-read a retry from the tick. Main thread only.</summary>
		private void OnArenaSeatReloadFailed(long instanceID, long matchID)
		{
			if (!arenaMatchesByInstance.TryGetValue(instanceID, out ArenaMatchState state) || state.MatchID != matchID)
			{
				return;
			}

			state.SeatReloadDue = true;
			state.NextSeatReloadAt = MonotonicClock.NowSeconds + ArenaReadRetrySeconds;
		}

		/// <summary>
		/// Retries the reads a match is owed: a seat re-read that failed, and ranked seats whose
		/// rating read failed. Main thread only; bounded by <see cref="ArenaReadMaxRetries"/> each.
		/// </summary>
		private void TickArenaReads(ArenaMatchState state, double now)
		{
			bool idle = Volatile.Read(ref state.SeatReloadInFlight) == 0;
			if (idle && state.SeatReloadDue && now >= state.NextSeatReloadAt)
			{
				if (state.SeatReloadRetries >= ArenaReadMaxRetries)
				{
					state.SeatReloadDue = false;
					Log.Error("InteractableSystem", $"Arena: gave up re-reading the seats of match {state.MatchID} after {state.SeatReloadRetries} retries; a backfill who arrived meanwhile has no seat here.");
				}
				else
				{
					++state.SeatReloadRetries;
					state.SeatReloadDue = false;
					if (!TryStartSeatReload(state))
					{
						OnArenaSeatReloadFailed(state.InstanceID, state.MatchID);
					}
				}
			}
			else if (idle && state.SeatReloadWanted)
			{
				// A stranger arrived while the last read was in flight: read again for them.
				TryStartSeatReload(state);
			}

			if (!state.Ranked || now < state.NextRatingReadAt || state.RatingReadRetries >= ArenaReadMaxRetries)
			{
				return;
			}

			List<long> unread = null;
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (!seat.Dropped && !seat.RatingLoaded)
				{
					(unread ??= new List<long>()).Add(seat.CharacterID);
				}
			}

			if (unread != null)
			{
				++state.RatingReadRetries;
				LoadArenaRatings(state, unread);
			}
		}

		/// <summary>A character left a scene. If it was an arena, they are no longer present in it.</summary>
		private void CharacterSystem_OnArenaCharacterDespawned(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character?.GameObject == null)
			{
				return;
			}

			if (!TryGetArenaMatchForScene(character.GameObject.scene.handle, out ArenaMatchState state) ||
				!state.Seats.TryGetValue(character.ID, out ArenaSeatState seat))
			{
				return;
			}

			seat.Present = false;
			SyncSeatConnection(state, seat, null);
			seat.RespawnAt = null;
			DropCarriedFlag(state, seat, character.Transform != null ? character.Transform.position : seat.LastPosition);

			if (state.Phase == ArenaMatchPhase.Live)
			{
				CheckArenaOutcome(state, timeUp: false);
			}

			if (state.Phase < ArenaMatchPhase.Ended)
			{
				BroadcastArenaState(state);
			}
		}

		/// <summary>
		/// A character is leaving a scene by any route. Leaving a live match forfeits it: the loss
		/// is written to their attributes now, while they are still in memory and about to be saved,
		/// and they are locked out of the queue. With a reconnect grace the seat is held, and a
		/// return inside it refunds both.
		/// </summary>
		/// <remarks>
		/// Fires for the match's own eviction too, but by then the phase is Ended and nothing here
		/// applies. A quit during gathering, the ready check or the countdown is not a forfeit — no
		/// match was played. A seat that forfeits is skipped by the end-of-match stats, so a leaver
		/// is never charged twice.
		/// </remarks>
		private void CharacterSystem_OnArenaCharacterLeft(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character?.GameObject == null ||
				!TryGetArenaMatchForScene(character.GameObject.scene.handle, out ArenaMatchState state) ||
				!state.Seats.TryGetValue(character.ID, out ArenaSeatState seat) ||
				seat.Dropped || seat.Forfeited || seat.DisconnectedAt.HasValue)
			{
				return;
			}

			if (state.Phase != ArenaMatchPhase.Live)
			{
				return;
			}

			seat.Present = false;
			SyncSeatConnection(state, seat, null);
			seat.RespawnAt = null;
			seat.Name = character.CharacterName;
			DropCarriedFlag(state, seat, character.Transform != null ? character.Transform.position : seat.LastPosition);

			int lossPoints = state.Template != null ? state.Template.LossRankPoints : 5;
			int winPoints = state.Template != null ? state.Template.WinRankPoints : 10;
			// A forfeit is a loss to whichever team is not theirs; any other team index will do.
			int notTheirTeam = seat.Team == 0 ? 1 : 0;
			seat.ForfeitRankDelta = ApplyPvPResult(character, seat.Team, notTheirTeam, winPoints, lossPoints);
			seat.ForfeitCharged = true;

			int lockMinutes = state.Template != null ? state.Template.DeserterLockMinutes : 15;
			LockArenaQueue(seat.CharacterID, lockMinutes, "Left a live arena match");

			int grace = state.Template != null ? state.Template.ReconnectGraceSeconds : 60;
			if (grace > 0)
			{
				seat.DisconnectedAt = MonotonicClock.NowSeconds;
				BroadcastArenaEvent(state, ArenaEventKind.PlayerDisconnected, null, seat, seat.Team, grace);
				Log.Debug("InteractableSystem", $"Arena: {character.CharacterName} left match {state.MatchID} while it was live; seat held for {grace}s.");
			}
			else
			{
				ForfeitSeat(state, seat);
			}

			if (!CheckArenaOutcome(state, timeUp: false))
			{
				BroadcastArenaState(state);
			}
		}

		/// <summary>Gives up a disconnected seat for good: vacated for backfill, announced, and out of the outcome.</summary>
		private void ForfeitSeat(ArenaMatchState state, ArenaSeatState seat)
		{
			seat.Forfeited = true;
			seat.Present = false;
			SyncSeatConnection(state, seat, null);
			seat.DisconnectedAt = null;
			seat.RespawnAt = null;

			long matchID = state.MatchID;
			long characterID = seat.CharacterID;
			PersistSeatVacated(matchID, characterID);

			BroadcastArenaEvent(state, ArenaEventKind.PlayerForfeited, null, seat, seat.Team, 0);
			Log.Debug("InteractableSystem", $"Arena: seat of character {characterID} in match {matchID} forfeited and vacated.");
		}

		/// <summary>
		/// Writes a seat as vacated: open for backfill, and no longer holding its player in the match.
		/// </summary>
		/// <remarks>
		/// Unvacated, the seat cannot be backfilled, the player is refused by both finders as "in a
		/// live match" until it ends, and a leaver's history does not show the desertion. Nothing
		/// else ever writes it.
		/// </remarks>
		private void PersistSeatVacated(long matchID, long characterID)
		{
			EnqueuePersistence(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
					{
						await Log.Warning("InteractableSystem", $"Arena: IArenaMatchService unavailable; the seat of character {characterID} in match {matchID} was not vacated.");
						return;
					}

					DatabaseResult<bool> vacated = await WriteArenaRowAsync(() => matchService.MarkSeatVacatedAsync(matchID, characterID));
					if (!vacated.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"Arena: the seat of character {characterID} in match {matchID} could not be vacated: [{vacated.ErrorCode}] {vacated.ErrorMessage}");
					}
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error vacating seat of character {characterID} in arena match {matchID}: {ex}");
				}
			}, matchID);
		}

		/// <summary>Undoes the attribute charges of a forfeit for a player who came back in time.</summary>
		private void RefundForfeit(IPlayerCharacter character, ArenaSeatState seat)
		{
			if (!seat.ForfeitCharged)
			{
				return;
			}
			seat.ForfeitCharged = false;
			AdjustPvPAttribute(character, PvPRankAttributeName, -seat.ForfeitRankDelta);
			AdjustPvPAttribute(character, PvPMatchesAttributeName, -1);
			AdjustPvPAttribute(character, PvPLossesAttributeName, -1);
			seat.ForfeitRankDelta = 0;
		}

		/// <summary>
		/// The match hosted in a scene, for the events that know only the scene. Main thread only.
		/// </summary>
		/// <remarks>
		/// Checked against the instance the scene holds now, not only its handle: Unity reuses a
		/// scene handle once its scene unloads, so a match whose instance was closed from elsewhere
		/// could otherwise be handed the kills, departures and objectives of whatever scene loaded
		/// next under the same handle, until the tick noticed.
		/// </remarks>
		private bool TryGetArenaMatchForScene(int sceneHandle, out ArenaMatchState state)
		{
			if (arenaInstanceBySceneHandle.TryGetValue(sceneHandle, out long instanceID) &&
				arenaMatchesByInstance.TryGetValue(instanceID, out state) &&
				IsArenaInstanceLoaded(sceneHandle, instanceID))
			{
				return true;
			}
			state = null;
			return false;
		}

		/// <summary>
		/// Whether the scene with this handle is still the instance with this row id. Main thread only.
		/// </summary>
		/// <remarks>
		/// The handle alone cannot say: it is the scene manager's, and it is given to the next scene
		/// that loads once this one has gone. The row id is the instance's own identity.
		/// </remarks>
		private bool IsArenaInstanceLoaded(int sceneHandle, long instanceID)
		{
			return Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) &&
				mappingData.SceneInstanceByHandle.TryGetValue(sceneHandle, out ISceneInstanceDetails details) &&
				details != null &&
				details.SceneID == instanceID;
		}

		/// <summary>
		/// Records the connection a seat's player is on — or null when they are not in the arena —
		/// and keeps their team's connection set in step with it. Main thread only.
		/// </summary>
		/// <remarks>
		/// The one writer of <see cref="ArenaSeatState.Connection"/> and of
		/// <see cref="ArenaMatchState.TeamConnections"/>. Called after every change to a seat's
		/// presence or its dropped mark — arrival, reseat, despawn, departure, forfeit — so the set
		/// is exactly the rule <see cref="ArenaRules.IsOnTeamChannel"/> applied to the seats, and a
		/// team send is one multicast instead of a walk of the server's characters.
		/// </remarks>
		private static void SyncSeatConnection(ArenaMatchState state, ArenaSeatState seat, NetworkConnection connection)
		{
			HashSet<NetworkConnection> team = state.TeamConnections != null && seat.Team >= 0 && seat.Team < state.TeamConnections.Length
				? state.TeamConnections[seat.Team]
				: null;

			if (seat.Connection != null && !ReferenceEquals(seat.Connection, connection))
			{
				team?.Remove(seat.Connection);
			}
			seat.Connection = connection;

			if (connection == null || team == null)
			{
				return;
			}
			if (ArenaRules.IsOnTeamChannel(seat.Present, seat.Dropped))
			{
				team.Add(connection);
			}
			else
			{
				team.Remove(connection);
			}
		}

		/// <summary>
		/// The connections of the arena team a character is seated on, in the match hosted in the
		/// scene they are standing in: every seat of that team whose player is in the arena now.
		/// Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// For team-only sends — team chat — as one multicast
		/// (<c>INetworkManagerWrapper.Broadcast(HashSet&lt;NetworkConnection&gt;, ...)</c>) rather than a walk
		/// of every character on the server. Membership is <see cref="ArenaRules.IsOnTeamChannel"/>:
		/// the team as <see cref="ArenaTeamRegistry"/> publishes it (no dropped seats), narrowed to
		/// the players present to receive it. Kept current on arrival, reseat, despawn, departure
		/// and forfeit.
		/// </para>
		/// <para>
		/// The set belongs to the coordinator: read it on the main thread, and never modify or keep
		/// it. It changes as players come and go, and it is emptied when the match closes.
		/// </para>
		/// </remarks>
		/// <param name="sceneHandle">The handle of the scene the character is standing in.</param>
		/// <param name="characterID">The character.</param>
		/// <param name="teamConnections">The team's connections, the character's own among them; null when false.</param>
		/// <returns>
		/// False when that scene hosts no match on this server, or the character holds no seat in
		/// it that is on its team's channel (no seat, a dropped seat, or not present).
		/// </returns>
		public bool TryGetArenaTeamConnections(int sceneHandle, long characterID, out HashSet<NetworkConnection> teamConnections)
		{
			teamConnections = null;
			if (!TryGetArenaMatchForScene(sceneHandle, out ArenaMatchState state) ||
				!state.Seats.TryGetValue(characterID, out ArenaSeatState seat) ||
				!ArenaRules.IsOnTeamChannel(seat.Present, seat.Dropped) ||
				state.TeamConnections == null || seat.Team < 0 || seat.Team >= state.TeamConnections.Length)
			{
				return false;
			}
			teamConnections = state.TeamConnections[seat.Team];
			return true;
		}

		// ──────────────────────────────────────────────────────────────────
		//  Kills
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Scores a kill inside a live match, feeds the announcer, and schedules the victim's respawn.</summary>
		private void CharacterDamageController_OnArenaKilled(ICharacter killer, ICharacter defender)
		{
			if (!(defender is IPlayerCharacter victim) || victim.GameObject == null)
			{
				return;
			}

			if (!TryGetArenaMatchForScene(victim.GameObject.scene.handle, out ArenaMatchState state) ||
				state.Phase != ArenaMatchPhase.Live ||
				!state.Seats.TryGetValue(victim.ID, out ArenaSeatState victimSeat) ||
				victimSeat.Dropped)
			{
				return;
			}

			victimSeat.Deaths += 1;
			victimSeat.Name = victim.CharacterName;
			int endedStreak = victimSeat.Streak;
			victimSeat.Streak = 0;
			DropCarriedFlag(state, victimSeat, victim.Transform != null ? victim.Transform.position : victimSeat.LastPosition);

			ArenaSeatState killerSeat = null;
			IPlayerCharacter attacker = killer as IPlayerCharacter;
			if (attacker != null &&
				state.Seats.TryGetValue(attacker.ID, out killerSeat) &&
				!killerSeat.Dropped &&
				killerSeat.Team != victimSeat.Team)
			{
				killerSeat.Kills += 1;
				killerSeat.Streak += 1;
				killerSeat.Name = attacker.CharacterName;
				if (state.Template == null || state.Template.Mode == ArenaMode.TeamDeathmatch)
				{
					killerSeat.Score += 1;
					state.TeamScores[killerSeat.Team] += 1;
					AnnounceNearLimit(state, killerSeat.Team);
				}

				BroadcastArenaEvent(state, ArenaEventKind.Kill, killerSeat, victimSeat, killerSeat.Team, 0);
				if (!state.FirstBloodDone)
				{
					state.FirstBloodDone = true;
					BroadcastArenaEvent(state, ArenaEventKind.FirstBlood, killerSeat, victimSeat, killerSeat.Team, 0);
				}
				int spree = state.Template != null ? state.Template.KillingSpreeThreshold : 3;
				if (spree > 0 && killerSeat.Streak >= spree)
				{
					BroadcastArenaEvent(state, ArenaEventKind.KillingSpree, killerSeat, null, killerSeat.Team, killerSeat.Streak);
				}
				if (spree > 0 && endedStreak >= spree)
				{
					BroadcastArenaEvent(state, ArenaEventKind.SpreeEnded, killerSeat, victimSeat, killerSeat.Team, endedStreak);
				}
			}
			else
			{
				killerSeat = null;
				BroadcastArenaEvent(state, ArenaEventKind.Kill, null, victimSeat, -1, 0);
			}

			int respawnSeconds = state.Template != null ? state.Template.RespawnSeconds : 0;
			victimSeat.RespawnAt = respawnSeconds > 0 ? MonotonicClock.NowSeconds + respawnSeconds : (double?)null;

			if (victim.Owner != null && victim.Owner.IsActive)
			{
				Server.NetworkWrapper.Broadcast(victim.Owner, new ArenaRespawnBroadcast
				{
					SecondsUntilRespawn = respawnSeconds,
					KillerID = killerSeat?.CharacterID ?? 0,
					KillerName = killerSeat?.Name ?? string.Empty,
					KillerTeam = killerSeat?.Team ?? -1,
				}, true, FishNet.Transporting.Channel.Reliable);
			}

			if (!CheckArenaOutcome(state, timeUp: false))
			{
				BroadcastArenaState(state);
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Tick
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Advances every hosted match by one second.</summary>
		/// <param name="deltaTime">
		/// Real seconds since the last tick. Unused, deliberately: every arena timer is a moment
		/// compared with now — phase ends, respawns, reconnect graces, dropped flags, and control
		/// point holds (<see cref="ArenaObjectiveState.HeldAccruedTo"/>) — so a tick that comes
		/// late, or one that stands for several after a hitch, loses nothing by being one tick.
		/// <para>
		/// Those moments are <see cref="MonotonicClock"/> seconds. Each is a local duration: on the
		/// wall clock a host stepped forward ended every phase, grace and respawn inside the step on
		/// one tick, and credited every held control point with the whole step. A deserter's queue lock
		/// is a duration the database adds to its own now.
		/// </para>
		/// </param>
		private void OnArenaTick(float deltaTime)
		{
			if (Server == null)
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;

			// Before the early out: a match whose read failed is exactly one that is not hosted yet.
			RetryArenaMatchLoads(now);

			if (arenaMatchesByInstance.Count == 0)
			{
				return;
			}

			List<ArenaMatchState> finished = null;

			foreach (ArenaMatchState state in arenaMatchesByInstance.Values.ToList())
			{
				/* The instance went away under us: a lifetime cap, a close from elsewhere. The match
				 * row must not stay open, or every seat is "in a live match" forever and locked out
				 * of both finders; a match that had not ended is recorded as cancelled. Judged by the
				 * instance's row id as well as the handle: a scene that loaded under the recycled
				 * handle is not this match's instance. */
				if (!IsArenaInstanceLoaded(state.SceneHandle, state.InstanceID))
				{
					if (state.Phase < ArenaMatchPhase.Ended)
					{
						Log.Warning("InteractableSystem", $"Arena: match {state.MatchID}'s instance disappeared while {state.Phase}; recording it as cancelled.");
						PersistArenaStatus(state, ArenaMatchStatus.Cancelled);
						ArenaServerEvents.RaiseMatchCancelled(state.MatchID, state.Template, "the instance disappeared");
					}
					(finished ??= new List<ArenaMatchState>()).Add(state);
					continue;
				}

				if (state.Phase < ArenaMatchPhase.Ended)
				{
					TickArenaReads(state, now);
				}

				switch (state.Phase)
				{
					case ArenaMatchPhase.Gathering:
						TickGathering(state, now);
						break;
					case ArenaMatchPhase.ReadyCheck:
						TickReadyCheck(state, now);
						break;
					case ArenaMatchPhase.Countdown:
						TickCountdown(state, now);
						break;
					case ArenaMatchPhase.Live:
						TickLive(state, now);
						break;
					case ArenaMatchPhase.Ended:
					case ArenaMatchPhase.Cancelled:
						if (now >= state.PhaseEndsAt)
						{
							CloseArenaMatch(state, state.Phase == ArenaMatchPhase.Ended ? "the match ended" : "the match was cancelled");
						}
						break;
				}
			}

			if (finished != null)
			{
				foreach (ArenaMatchState state in finished)
				{
					ForgetArenaMatch(state);
				}
			}
		}

		private void TickGathering(ArenaMatchState state, double now)
		{
			bool allPresent = true;
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (!seat.Dropped && !seat.Present)
				{
					allPresent = false;
					break;
				}
			}

			if (allPresent)
			{
				BeginReadyCheckOrCountdown(state);
				return;
			}

			if (now < state.PhaseEndsAt)
			{
				return;
			}

			/* Waited long enough. Whoever has not arrived is out of the match: they were told they
			 * were matched, their transfer either never happened or failed, and the people who did
			 * arrive should not wait forever. Their queue row, if any, has already been deleted by
			 * their own server's transfer path. */
			int dropped = 0;
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (!seat.Present && !seat.Dropped)
				{
					seat.Dropped = true;
					++dropped;
				}
			}
			if (dropped > 0)
			{
				Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} dropped {dropped} seats that never arrived.");
				PublishArenaRoster(state);
			}

			if (CountTeamsWithPlayers(state) < 2)
			{
				CancelArenaMatch(state, "not enough players arrived");
				return;
			}

			BeginReadyCheckOrCountdown(state);
		}

		private void TickReadyCheck(ArenaMatchState state, double now)
		{
			/* Everyone accepted since the last tick: the countdown starts here, inside the tick,
			 * never from the answer's handler. A countdown started at an arbitrary moment put
			 * every later tick at an arbitrary point within its seconds, and at some points the
			 * frame's jitter decided which second each tick saw. Started on the tick, every tick
			 * lands on a whole second (see ArenaRules.ResolveTickSecond). */
			if (AllSeatsReady(state))
			{
				Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} everyone accepted.");
				BeginArenaCountdown(state);
				return;
			}

			int seconds = ArenaRules.ResolveTickSecond(state.PhaseEndsAt - now);
			if (seconds != state.LastBroadcastSecond)
			{
				state.LastBroadcastSecond = seconds;
				BroadcastReadyCheck(state, seconds);
			}

			if (seconds > 0)
			{
				return;
			}

			// Time is up. Whoever did not answer is treated as a decline.
			int lockMinutes = state.Template != null ? state.Template.DeclineLockMinutes : 5;
			var silent = new List<string>();
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Dropped || seat.Answered)
				{
					continue;
				}
				silent.Add(seat.Name ?? seat.CharacterID.ToString());
				LockArenaQueue(seat.CharacterID, lockMinutes, "Did not answer an arena ready check");
			}

			CancelArenaMatch(state, silent.Count > 0 ? $"{string.Join(", ", silent)} did not accept" : "the ready check timed out");
		}

		private void TickCountdown(ArenaMatchState state, double now)
		{
			/* Every second passed since the last announcement, in order: each carries its own
			 * cues, and one skipped is a cue that never plays. Ordinarily one per tick; after a
			 * hitch, the ones it swallowed, back to back. */
			int seconds = ArenaRules.ResolveTickSecond(state.PhaseEndsAt - now);
			int steps = ArenaRules.ResolveCountdownSteps(state.LastBroadcastSecond, seconds, out int highest);
			for (int i = 0; i < steps; ++i)
			{
				BroadcastArenaState(state, highest - i);
			}
			if (steps > 0)
			{
				state.LastBroadcastSecond = seconds;
			}

			if (seconds <= 0)
			{
				GoLive(state);
			}
		}

		private void TickLive(ArenaMatchState state, double now)
		{
			bool changed = false;

			Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping);

			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				// Reconnect grace run out.
				if (seat.DisconnectedAt.HasValue && !seat.Forfeited)
				{
					int grace = state.Template != null ? state.Template.ReconnectGraceSeconds : 60;
					if (now >= seat.DisconnectedAt.Value + grace)
					{
						ForfeitSeat(state, seat);
						changed = true;
					}
				}

				// Respawns due.
				if (charMapping != null && seat.RespawnAt.HasValue && now >= seat.RespawnAt.Value && seat.Present)
				{
					seat.RespawnAt = null;
					if (charMapping.CharactersByID.TryGetValue(seat.CharacterID, out IPlayerCharacter character) &&
						character?.GameObject != null && character.GameObject.scene.handle == state.SceneHandle &&
						character.IsFlagged(CharacterFlags.IsDead))
					{
						// Still dead. A teammate's resurrection in the meantime leaves them where they stand.
						RespawnInArena(state, character, seat.Team);
					}
				}

				if (charMapping != null && seat.Present &&
					charMapping.CharactersByID.TryGetValue(seat.CharacterID, out IPlayerCharacter present) && present?.Transform != null)
				{
					seat.LastPosition = present.Transform.position;
				}
			}

			if (TickDroppedFlags(state, now, charMapping))
			{
				changed = true;
			}

			bool scored = TickControlPoints(state, now);

			bool timed = state.Template != null && state.Template.MatchMinutes > 0;
			int seconds = timed ? ArenaRules.ResolveTickSecond(state.PhaseEndsAt - now) : 0;
			bool timeUp = timed && seconds <= 0;

			/* Every warning the clock passed since the last tick, not only one whose exact second
			 * this tick happened to see: a late tick stepped over a second, and its warning was
			 * lost. */
			if (timed && state.Template.TimeWarningSeconds != null)
			{
				foreach (int warning in state.Template.TimeWarningSeconds)
				{
					if (ArenaRules.IsThresholdCrossed(state.LastWarningCheckSecond, seconds, warning) && state.TimeWarningsFired.Add(warning))
					{
						BroadcastArenaEvent(state, ArenaEventKind.TimeWarning, null, null, -1, warning);
					}
				}
				state.LastWarningCheckSecond = seconds;
			}

			if (CheckArenaOutcome(state, timeUp))
			{
				return;
			}

			if (changed || scored || (timed && seconds != state.LastBroadcastSecond))
			{
				state.LastBroadcastSecond = seconds;
				BroadcastArenaState(state, seconds);
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Ready check
		// ──────────────────────────────────────────────────────────────────

		private void BeginReadyCheckOrCountdown(ArenaMatchState state)
		{
			int seconds = state.Template != null ? state.Template.ReadyCheckSeconds : 20;
			if (seconds <= 0)
			{
				BeginArenaCountdown(state);
				return;
			}

			state.Phase = ArenaMatchPhase.ReadyCheck;
			state.PhaseEndsAt = MonotonicClock.NowSeconds + seconds;
			state.LastBroadcastSecond = -1;
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				seat.Answered = false;
				seat.Ready = false;
			}

			PersistArenaStatus(state, ArenaMatchStatus.ReadyCheck);
			Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} ready check, {seconds}s.");
			BroadcastArenaState(state, seconds);
			BroadcastReadyCheck(state, seconds);
			state.LastBroadcastSecond = seconds;
		}

		/// <summary>A player answered the ready check.</summary>
		private void OnServerArenaReadyResponseReceived(NetworkConnection conn, ArenaReadyResponseBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn?.FirstObject == null)
			{
				return;
			}

			/* One answer per seat is enforced below by seat.Answered; this bounds what an
			 * unanswered seat can cost per packet before that check. Synchronous handler. */
			if (!TryBeginIngressGuard(conn.ClientId, ArenaReadyOperation, ArenaReadyDebounceMilliseconds, out long readyGuardKey))
			{
				return;
			}
			EndIngressGuard(readyGuardKey);

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null || !CharacterStateValidation.CanAct(character))
			{
				return;
			}
			if (character?.GameObject == null ||
				!TryGetArenaMatchForScene(character.GameObject.scene.handle, out ArenaMatchState state) ||
				state.MatchID != msg.MatchID ||
				state.Phase != ArenaMatchPhase.ReadyCheck ||
				!state.Seats.TryGetValue(character.ID, out ArenaSeatState seat) ||
				seat.Dropped || seat.Answered)
			{
				return;
			}

			seat.Answered = true;
			seat.Ready = msg.Accept;
			seat.Name = character.CharacterName;

			if (!msg.Accept)
			{
				int lockMinutes = state.Template != null ? state.Template.DeclineLockMinutes : 5;
				LockArenaQueue(seat.CharacterID, lockMinutes, "Declined an arena ready check");
				CancelArenaMatch(state, $"{character.CharacterName} declined");
				return;
			}

			/* The last acceptance does not start the countdown here: the next tick does, at most a
			 * second from now (TickReadyCheck), so the countdown is on the tick's grid. Everyone is
			 * told the new tally at once either way. */
			int seconds = Math.Max(0, (int)Math.Ceiling(state.PhaseEndsAt - MonotonicClock.NowSeconds));
			BroadcastReadyCheck(state, seconds);
			BroadcastArenaState(state, seconds);
		}

		/// <summary>Whether every seat still in the match has accepted the ready check.</summary>
		private static bool AllSeatsReady(ArenaMatchState state)
		{
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (!seat.Dropped && !seat.Ready)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Sends each present seat its own view of the ready check: whether they have answered differs per seat.</summary>
		private void BroadcastReadyCheck(ArenaMatchState state, int seconds)
		{
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Dropped || !seat.Present || seat.Connection == null || !seat.Connection.IsActive)
				{
					continue;
				}
				SendReadyCheck(state, seat, seat.Connection, seconds);
			}
		}

		private void SendReadyCheck(ArenaMatchState state, ArenaSeatState seat, NetworkConnection conn, int seconds = -1)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}
			if (seconds < 0)
			{
				seconds = Math.Max(0, (int)Math.Ceiling(state.PhaseEndsAt - MonotonicClock.NowSeconds));
			}
			int accepted = 0, total = 0;
			foreach (ArenaSeatState other in state.Seats.Values)
			{
				if (other.Dropped)
				{
					continue;
				}
				++total;
				if (other.Ready)
				{
					++accepted;
				}
			}
			Server.NetworkWrapper.Broadcast(conn, new ArenaReadyCheckBroadcast
			{
				MatchID = state.MatchID,
				SecondsRemaining = seconds,
				Accepted = accepted,
				Total = total,
				YouAnswered = seat.Answered,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		// ──────────────────────────────────────────────────────────────────
		//  Phase changes
		// ──────────────────────────────────────────────────────────────────

		private void BeginArenaCountdown(ArenaMatchState state)
		{
			state.Phase = ArenaMatchPhase.Countdown;
			int seconds = state.Template != null ? Math.Max(1, state.Template.CountdownSeconds) : 10;
			state.PhaseEndsAt = MonotonicClock.NowSeconds + seconds;
			state.LastBroadcastSecond = -1;
			ArenaTeamRegistry.SetLive(state.SceneHandle, false);
			DiscoverArenaObjectives(state);
			ResetArenaObjectives(state);

			// Everyone to their corners, alive and at full health.
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				foreach (ArenaSeatState seat in state.Seats.Values)
				{
					if (seat.Present && !seat.Dropped &&
						charMapping.CharactersByID.TryGetValue(seat.CharacterID, out IPlayerCharacter character))
					{
						RespawnInArena(state, character, seat.Team);
					}
				}
			}

			PersistArenaStatus(state, ArenaMatchStatus.Countdown);
			Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} counting down from {seconds}.");
			BroadcastArenaState(state, seconds);
			state.LastBroadcastSecond = seconds;
		}

		private void GoLive(ArenaMatchState state)
		{
			state.Phase = ArenaMatchPhase.Live;
			bool timed = state.Template != null && state.Template.MatchMinutes > 0;
			state.PhaseEndsAt = timed ? MonotonicClock.NowSeconds + state.Template.MatchMinutes * 60.0 : double.PositiveInfinity;
			state.LastBroadcastSecond = -1;
			// One past the full clock, so a warning authored at the match's own length fires once, on the first tick.
			state.LastWarningCheckSecond = timed ? state.Template.MatchMinutes * 60 + 1 : int.MaxValue;
			ArenaTeamRegistry.SetLive(state.SceneHandle, true);

			/* Anyone who did not make it to the countdown is out for good, and their seat opens.
			 *
			 * A seat dropped at the gathering timeout is the same player — they never arrived — so
			 * its row is vacated here too. It used to stay Seated for the whole match: the seat could
			 * not be backfilled, and both finders refused its player as "in a live match" until the
			 * match ended. It is vacated quietly rather than forfeited: failing to arrive is a failed
			 * transfer as often as a choice, and the gathering timeout already told the room. Before
			 * now a dropped seat can still be reclaimed by arriving during the countdown, which is
			 * why the write waits for this moment. */
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Forfeited)
				{
					continue;
				}
				if (seat.Dropped)
				{
					PersistSeatVacated(state.MatchID, seat.CharacterID);
				}
				else if (!seat.Present)
				{
					ForfeitSeat(state, seat);
				}
			}

			int backfill = state.Template != null ? state.Template.BackfillWindowSeconds : 60;

			PersistArenaStatus(state, ArenaMatchStatus.Live);
			if (backfill > 0)
			{
				long matchID = state.MatchID;
				/* A length, not a moment: the database stamps the window's end on its own clock,
				 * because the backfill transactions that read it run on other scene servers and
				 * compare it with the database's clock. */
				TimeSpan window = TimeSpan.FromSeconds(backfill);
				EnqueuePersistence(async () =>
				{
					try
					{
						if (Server?.Database?.ServiceRegistry == null ||
							!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
						{
							await Log.Warning("InteractableSystem", $"Arena: IArenaMatchService unavailable; match {matchID} opens no backfill window.");
							return;
						}

						// The window is what the queue's backfill reads; without it no vacated seat is ever filled.
						DatabaseResult<bool> opened = await WriteArenaRowAsync(() => matchService.SetBackfillWindowAsync(matchID, window));
						if (!opened.IsSuccess)
						{
							await Log.Warning("InteractableSystem", $"Arena: the backfill window of match {matchID} could not be opened: [{opened.ErrorCode}] {opened.ErrorMessage}");
						}
					}
					catch (Exception ex)
					{
						await Log.Error("InteractableSystem", $"Error opening the backfill window of arena match {matchID}: {ex}");
					}
				}, matchID);
			}

			Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} is live.");
			BroadcastArenaState(state, timed ? state.Template.MatchMinutes * 60 : 0);
		}

		/// <summary>Ends the match if the rules say so. Returns true when it ended.</summary>
		private bool CheckArenaOutcome(ArenaMatchState state, bool timeUp)
		{
			int scoreLimit = state.Template != null ? state.Template.ScoreLimit : 0;
			int teamsWithPlayers = CountTeamsWithPlayers(state);

			if (!ArenaRules.ResolveOutcome(state.TeamScores, scoreLimit, timeUp, teamsWithPlayers, out int winner))
			{
				return false;
			}

			if (winner == -2)
			{
				// Walkover: the only team still standing.
				winner = -1;
				for (int t = 0; t < state.TeamCount; ++t)
				{
					if (TeamHasPlayers(state, t))
					{
						winner = t;
						break;
					}
				}
			}

			EndArenaMatch(state, winner);
			return true;
		}

		private void EndArenaMatch(ArenaMatchState state, int winnerTeam)
		{
			state.Phase = ArenaMatchPhase.Ended;
			state.WinnerTeam = winnerTeam;
			int resultsSeconds = state.Template != null ? Math.Max(3, state.Template.ResultsSeconds) : 15;
			state.PhaseEndsAt = MonotonicClock.NowSeconds + resultsSeconds;
			ArenaTeamRegistry.SetLive(state.SceneHandle, false);

			Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} ended; winner team {winnerTeam}; scores {string.Join("/", state.TeamScores)}.");

			// A disconnected seat still inside its grace loses now; the match is over.
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.DisconnectedAt.HasValue && !seat.Forfeited)
				{
					seat.Forfeited = true;
					seat.DisconnectedAt = null;
				}
			}

			// Placements by score only.
			var lines = new List<ArenaPlacement>(state.Seats.Count);
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Dropped || seat.Forfeited)
				{
					continue;
				}
				lines.Add(new ArenaPlacement { CharacterID = seat.CharacterID, Team = seat.Team, Kills = seat.Kills, Deaths = seat.Deaths, Score = seat.Score });
			}
			List<ArenaPlacement> placements = ArenaRules.ResolvePlacements(lines);
			var placementByCharacter = new Dictionary<long, int>(placements.Count);
			var placementEntries = new ArenaMemberEntry[placements.Count];
			for (int i = 0; i < placements.Count; ++i)
			{
				ArenaPlacement p = placements[i];
				placementByCharacter[p.CharacterID] = i + 1;
				placementEntries[i] = new ArenaMemberEntry { CharacterID = p.CharacterID, Team = p.Team, Kills = p.Kills, Deaths = p.Deaths, Score = p.Score, Present = state.Seats[p.CharacterID].Present };
			}

			// Ratings, for a ranked match. Forfeits are scored as losses whatever their team did.
			var ratingResults = new List<(long characterId, int newRating, bool won)>();
			if (state.Ranked)
			{
				int placementGames = state.Template != null ? state.Template.PlacementGames : 10;
				int k = state.Template != null ? state.Template.RatingK : 32;
				int placementK = state.Template != null ? state.Template.PlacementK : 64;

				/* Only seats whose rating was actually read are rated, and only they stand in the
				 * opponent averages.
				 *
				 * An unread seat used to be rated from the default, and the write below replaces the
				 * stored rating rather than applying a change to it — so a read that failed reset the
				 * player's season rating. Leaving them unrated costs them one match's change; guessing
				 * cost them their standing. They are kept out of everyone else's opponent average for
				 * the same reason: their real rating is unknown, and the default is not it. */
				var fair = new List<(long characterId, int team, int rating, int games)>();
				var teamRatings = new List<(int team, int rating)>();
				int unrated = 0;
				foreach (ArenaSeatState seat in state.Seats.Values)
				{
					if (seat.Dropped)
					{
						continue;
					}
					if (!seat.RatingLoaded)
					{
						++unrated;
						continue;
					}
					teamRatings.Add((seat.Team, seat.RatingBefore));
					if (!seat.Forfeited)
					{
						fair.Add((seat.CharacterID, seat.Team, seat.RatingBefore, seat.GamesBefore));
					}
				}
				if (unrated > 0)
				{
					Log.Warning("InteractableSystem", $"Arena: match {state.MatchID} is ranked but {unrated} seats' ratings were never read; those seats are not rated.");
				}

				foreach (var line in ArenaRating.Resolve(fair, winnerTeam, placementGames, k, placementK))
				{
					ArenaSeatState seat = state.Seats[line.characterId];
					seat.RatingDelta = line.delta;
					seat.NewRating = line.newRating;
					ratingResults.Add((seat.CharacterID, seat.NewRating, winnerTeam >= 0 && seat.Team == winnerTeam));
				}
				foreach (ArenaSeatState seat in state.Seats.Values)
				{
					if (seat.Dropped || !seat.Forfeited || !seat.RatingLoaded)
					{
						continue;
					}
					int opp = ArenaRating.OpponentAverage(teamRatings, seat.Team);
					int kf = ArenaRating.KFactor(seat.GamesBefore, placementGames, k, placementK);
					seat.RatingDelta = ArenaRating.Delta(seat.RatingBefore, opp, 0.0, kf);
					seat.NewRating = ArenaRating.Apply(seat.RatingBefore, seat.RatingDelta);
					ratingResults.Add((seat.CharacterID, seat.NewRating, false));
				}
			}

			// Stats and results, for everyone still here.
			int winPoints = state.Template != null ? state.Template.WinRankPoints : 10;
			int lossPoints = state.Template != null ? state.Template.LossRankPoints : 5;
			int placementGamesTotal = state.Template != null ? state.Template.PlacementGames : 10;
			var resultMembers = new List<ArenaMatchResultMember>(state.Seats.Count);
			Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping);

			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Dropped)
				{
					continue;
				}

				IPlayerCharacter character = null;
				bool here = !seat.Forfeited && seat.Present && charMapping != null &&
					charMapping.CharactersByID.TryGetValue(seat.CharacterID, out character) &&
					character?.Owner != null && character.Owner.IsActive;

				int rankDelta = 0;
				if (here)
				{
					rankDelta = ApplyPvPResult(character, seat.Team, winnerTeam, winPoints, lossPoints);
				}
				else if (seat.Forfeited)
				{
					rankDelta = seat.ForfeitRankDelta;
				}

				placementByCharacter.TryGetValue(seat.CharacterID, out int placement);
				resultMembers.Add(new ArenaMatchResultMember
				{
					CharacterID = seat.CharacterID,
					Character = here ? character : null,
					Team = seat.Team,
					Kills = seat.Kills,
					Deaths = seat.Deaths,
					Score = seat.Score,
					Placement = placement,
					Won = winnerTeam >= 0 && seat.Team == winnerTeam && !seat.Forfeited,
					Forfeited = seat.Forfeited,
					RankDelta = rankDelta,
					RatingDelta = seat.RatingDelta,
				});

				if (here)
				{
					// An unrated seat is shown the match as unranked rather than a rating of 0.
					bool rated = state.Ranked && seat.RatingLoaded;
					Server.NetworkWrapper.Broadcast(character.Owner, new ArenaResultsBroadcast
					{
						ArenaTemplateID = state.Template != null ? state.Template.ID : 0,
						Format = state.Format,
						WinnerTeam = winnerTeam,
						TeamScores = (int[])state.TeamScores.Clone(),
						Placements = placementEntries,
						RankDelta = rankDelta,
						Ranked = rated,
						RatingDelta = rated ? seat.RatingDelta : 0,
						NewRating = rated ? seat.NewRating : 0,
						PlacementGamesRemaining = rated ? ArenaRating.PlacementGamesRemaining(seat.GamesBefore + 1, placementGamesTotal) : 0,
					}, true, FishNet.Transporting.Channel.Reliable);
				}
			}

			BroadcastArenaState(state);

			// The reward hook: the event, then the template's trigger lists.
			RaiseArenaRewards(state, winnerTeam, resultMembers);

			// Tallies, ratings and the result, written together.
			var tallies = new List<(long, int, int, int)>(state.Seats.Count);
			var deltas = new List<(long, int)>(state.Seats.Count);
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				tallies.Add((seat.CharacterID, seat.Kills, seat.Deaths, seat.Score));
				if (state.Ranked && !seat.Dropped && seat.RatingLoaded)
				{
					deltas.Add((seat.CharacterID, seat.RatingDelta));
				}
			}
			long matchID = state.MatchID;
			long seasonID = state.SeasonID;
			bool ranked = state.Ranked;
			EnqueuePersistence(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
					{
						await Log.Error("InteractableSystem", $"Arena: IArenaMatchService unavailable; the result of match {matchID} was not recorded.");
						return;
					}

					/* Every write is checked. None of them was, so a failure anywhere here lost the
					 * result without a trace — and the players had already been shown it. */
					DatabaseResult<int> talliesResult = await WriteArenaRowAsync(() => matchService.UpdateMemberTalliesAsync(matchID, tallies));
					if (!talliesResult.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"Arena: the tallies of match {matchID} were not written: [{talliesResult.ErrorCode}] {talliesResult.ErrorMessage}");
					}

					if (ranked)
					{
						if (seasonID <= 0)
						{
							await Log.Warning("InteractableSystem", $"Arena: match {matchID} is ranked but no season was ever resolved; its ratings were shown but not written.");
						}
						else if (!Server.Database.ServiceRegistry.TryGet<IArenaRatingService>(out var ratingService))
						{
							await Log.Error("InteractableSystem", $"Arena: IArenaRatingService unavailable; the ratings of match {matchID} were not written.");
						}
						else
						{
							/* Not retried. The upsert counts the game — games, wins and losses are
							 * incremented — so repeating one whose outcome is unknown could count it
							 * twice. A failure is logged with everything needed to apply it by hand. */
							DatabaseResult<int> upsert = await ratingService.UpsertRatingsAsync(seasonID, ratingResults);
							if (!upsert.IsSuccess)
							{
								await Log.Error("InteractableSystem",
									$"Arena: the ratings of match {matchID} (season {seasonID}) were not written: [{upsert.ErrorCode}] {upsert.ErrorMessage}. " +
									$"Unwritten (character, newRating, won): {string.Join("; ", ratingResults)}");
							}

							DatabaseResult<int> deltasResult = await WriteArenaRowAsync(() => matchService.UpdateMemberRatingDeltasAsync(matchID, deltas));
							if (!deltasResult.IsSuccess)
							{
								await Log.Warning("InteractableSystem", $"Arena: the rating changes of match {matchID}'s seats were not written: [{deltasResult.ErrorCode}] {deltasResult.ErrorMessage}");
							}
						}
					}

					await WriteArenaStatusAsync(matchService, matchID, ArenaMatchStatus.Ended, winnerTeam);
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error recording arena match {matchID}: {ex}");
				}
			}, matchID);
		}

		/// <summary>
		/// Raises <see cref="ArenaServerEvents.OnMatchEnded"/> and runs the template's reward
		/// triggers on every present member. Nothing is granted here; see the events' remarks.
		/// </summary>
		private void RaiseArenaRewards(ArenaMatchState state, int winnerTeam, List<ArenaMatchResultMember> members)
		{
			try
			{
				ArenaServerEvents.RaiseMatchEnded(new ArenaMatchResult
				{
					MatchID = state.MatchID,
					Template = state.Template,
					Format = state.Format,
					Ranked = state.Ranked,
					WinnerTeam = winnerTeam,
					TeamScores = (int[])state.TeamScores.Clone(),
					Members = members,
				});
			}
			catch (Exception ex)
			{
				Log.Error("InteractableSystem", $"Arena: a match-ended subscriber threw for match {state.MatchID}: {ex}");
			}

			if (state.Template == null)
			{
				return;
			}

			foreach (ArenaMatchResultMember member in members)
			{
				if (member.Character == null || member.Forfeited)
				{
					continue;
				}

				List<Trigger> triggers = winnerTeam < 0
					? state.Template.DrawRewardTriggers
					: (member.Won ? state.Template.WinRewardTriggers : state.Template.LossRewardTriggers);
				if (triggers == null || triggers.Count == 0)
				{
					continue;
				}

				var eventData = new ArenaEventData(member.Character, ArenaCuePhase.Reward, 0, member.Team, winnerTeam, ArenaEventKind.Kill, member.Won, false, member.Placement);
				foreach (Trigger trigger in triggers)
				{
					if (trigger == null)
					{
						continue;
					}
					try
					{
						trigger.Execute(eventData);
					}
					catch (Exception ex)
					{
						Log.Error("InteractableSystem", $"Arena: reward trigger '{trigger.name}' threw for character {member.CharacterID} in match {state.MatchID}: {ex}");
					}
				}
			}
		}

		private void CancelArenaMatch(ArenaMatchState state, string reason)
		{
			state.Phase = ArenaMatchPhase.Cancelled;
			state.PhaseEndsAt = MonotonicClock.NowSeconds + ArenaCancelledSeconds;
			ArenaTeamRegistry.SetLive(state.SceneHandle, false);

			Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} cancelled: {reason}.");
			PersistArenaStatus(state, ArenaMatchStatus.Cancelled);
			ArenaServerEvents.RaiseMatchCancelled(state.MatchID, state.Template, reason);

			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Present && seat.Connection != null)
				{
					SendSystemMessage(seat.Connection, $"The match was cancelled: {reason}. You will be returned to the world.");
				}
			}

			BroadcastArenaState(state);
		}

		/// <summary>Returns everyone to the world and unloads the instance. The dungeon's close path.</summary>
		private void CloseArenaMatch(ArenaMatchState state, string reason)
		{
			ForgetArenaMatch(state);

			if (Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				sceneServerSystem.CloseInstance(state.InstanceID, reason);
			}
		}

		private void ForgetArenaMatch(ArenaMatchState state)
		{
			arenaMatchesByInstance.Remove(state.InstanceID);

			/* The handle's entries are removed only while they are still this match's. A match whose
			 * instance vanished under it is forgotten a tick later, by which time the recycled handle
			 * may belong to the next arena, whose mapping and published roster must survive this. */
			if (arenaInstanceBySceneHandle.TryGetValue(state.SceneHandle, out long mappedInstanceID) &&
				mappedInstanceID == state.InstanceID)
			{
				arenaInstanceBySceneHandle.Remove(state.SceneHandle);
				ArenaTeamRegistry.Unpublish(state.SceneHandle);
			}

			if (state.TeamConnections != null)
			{
				foreach (HashSet<NetworkConnection> team in state.TeamConnections)
				{
					team.Clear();
				}
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Objectives: Capture the Flag and King of the Hill
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Finds the flag stands and control points standing in the match's scene.
		/// </summary>
		/// <remarks>
		/// Scene objects register themselves with <c>SceneObject.Objects</c> as they spawn, so a
		/// scan at registration can miss objects that spawn a frame later; the scan is repeated at
		/// the countdown, and an interaction with an objective not yet known adds it then. Deathmatch
		/// arenas ignore any objectives they happen to contain.
		/// </remarks>
		private void DiscoverArenaObjectives(ArenaMatchState state)
		{
			if (state.Mode == ArenaMode.TeamDeathmatch)
			{
				return;
			}

			foreach (ISceneObject sceneObject in SceneObject.Objects.Values)
			{
				if (sceneObject?.GameObject == null || sceneObject.GameObject.scene.handle != state.SceneHandle)
				{
					continue;
				}

				IArenaObjective objective = sceneObject.GameObject.GetComponent<IArenaObjective>();
				if (objective != null)
				{
					EnsureArenaObjective(state, objective);
				}
			}
		}

		private ArenaObjectiveState EnsureArenaObjective(ArenaMatchState state, IArenaObjective objective)
		{
			if (!state.Objectives.TryGetValue(objective.ID, out ArenaObjectiveState tracked))
			{
				tracked = new ArenaObjectiveState
				{
					ObjectiveID = objective.ID,
					Kind = objective.Kind,
					Team = objective.Kind == ArenaObjectiveKind.FlagStand ? Mathf.Clamp(objective.Team, 0, state.TeamCount - 1) : -1,
				};
				state.Objectives[objective.ID] = tracked;
			}
			return tracked;
		}

		/// <summary>Puts every flag home and every control point neutral. Play has not started.</summary>
		private static void ResetArenaObjectives(ArenaMatchState state)
		{
			foreach (ArenaObjectiveState objective in state.Objectives.Values)
			{
				objective.Flag = ArenaFlagState.Home;
				objective.CarrierCharacterID = 0;
				objective.DropPosition = Vector3.zero;
				if (objective.Kind == ArenaObjectiveKind.ControlPoint)
				{
					objective.Team = -1;
				}
				objective.ProgressTeam = -1;
				objective.Progress = 0;
				objective.HeldSeconds = 0.0;
			}
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				seat.CarriedFlagObjectiveID = 0;
			}
		}

		/// <summary>A player interacted with a flag stand or control point. Main thread.</summary>
		private void ArenaObjective_OnServerInteracted(IPlayerCharacter player, IArenaObjective objective)
		{
			if (player?.GameObject == null || objective?.GameObject == null ||
				!TryGetArenaMatchForScene(player.GameObject.scene.handle, out ArenaMatchState state) ||
				objective.GameObject.scene.handle != state.SceneHandle ||
				state.Phase != ArenaMatchPhase.Live ||
				!state.Seats.TryGetValue(player.ID, out ArenaSeatState seat) ||
				seat.Dropped || seat.Forfeited || !seat.Present ||
				player.IsFlagged(CharacterFlags.IsDead))
			{
				return;
			}

			ArenaObjectiveState tracked = EnsureArenaObjective(state, objective);
			bool changed = false;
			seat.Name = player.CharacterName;

			if (tracked.Kind == ArenaObjectiveKind.FlagStand && state.Mode == ArenaMode.CaptureTheFlag)
			{
				switch (ArenaRules.ResolveFlagInteraction(tracked.Team, tracked.Flag, seat.Team, seat.CarriedFlagObjectiveID != 0))
				{
					case ArenaFlagAction.PickUp:
						tracked.Flag = ArenaFlagState.Carried;
						tracked.CarrierCharacterID = player.ID;
						seat.CarriedFlagObjectiveID = tracked.ObjectiveID;
						changed = true;
						BroadcastArenaEvent(state, ArenaEventKind.FlagTaken, seat, null, tracked.Team, 0);
						Log.Debug("InteractableSystem", $"Arena: {player.CharacterName} took team {tracked.Team + 1}'s flag in match {state.MatchID}.");
						break;

					case ArenaFlagAction.Capture:
						if (state.Objectives.TryGetValue(seat.CarriedFlagObjectiveID, out ArenaObjectiveState carried))
						{
							carried.Flag = ArenaFlagState.Home;
							carried.CarrierCharacterID = 0;
						}
						seat.CarriedFlagObjectiveID = 0;
						int points = state.Template != null ? Math.Max(1, state.Template.FlagCaptureScore) : 1;
						seat.Score += points;
						state.TeamScores[seat.Team] += points;
						changed = true;
						BroadcastArenaEvent(state, ArenaEventKind.FlagCaptured, seat, null, seat.Team, points);
						AnnounceNearLimit(state, seat.Team);
						Log.Debug("InteractableSystem", $"Arena: {player.CharacterName} captured a flag for team {seat.Team + 1} in match {state.MatchID}.");
						break;
				}
			}
			else if (tracked.Kind == ArenaObjectiveKind.ControlPoint && state.Mode == ArenaMode.KingOfTheHill)
			{
				int needed = state.Template != null ? state.Template.ControlPointCaptureInteractions : 3;
				ArenaControlPointResult result = ArenaRules.ResolveControlPointInteraction(tracked.Team, tracked.ProgressTeam, tracked.Progress, seat.Team, needed);
				if (result.OwnerTeam != tracked.Team || result.ProgressTeam != tracked.ProgressTeam || result.Progress != tracked.Progress)
				{
					changed = true;
				}
				tracked.Team = result.OwnerTeam;
				tracked.ProgressTeam = result.ProgressTeam;
				tracked.Progress = result.Progress;
				if (result.Captured)
				{
					tracked.HeldSeconds = 0.0;
					tracked.HeldAccruedTo = MonotonicClock.NowSeconds;
					seat.Score += state.Template != null ? state.Template.ControlPointCaptureScore : 5;
					BroadcastArenaEvent(state, ArenaEventKind.PointCaptured, seat, null, seat.Team, 0);
					Log.Debug("InteractableSystem", $"Arena: {player.CharacterName} captured a control point for team {seat.Team + 1} in match {state.MatchID}.");
				}
			}

			if (changed && !CheckArenaOutcome(state, timeUp: false))
			{
				BroadcastArenaState(state);
			}
		}

		/// <summary>
		/// Scores held control points for the real time held since the last accrual. Returns true
		/// when a team scored.
		/// </summary>
		/// <remarks>
		/// Held time is measured from each point's own capture moment and never past the match's
		/// end, so a point taken just before a tick is credited the moment it was held, and a
		/// hitch that swallowed several ticks — or one that runs past the final whistle — is
		/// credited exactly the time that fell inside the match.
		/// </remarks>
		private bool TickControlPoints(ArenaMatchState state, double now)
		{
			if (state.Mode != ArenaMode.KingOfTheHill)
			{
				return false;
			}

			int perPoint = state.Template != null ? Math.Max(1, state.Template.ControlPointHoldSecondsPerPoint) : 1;
			double accrueTo = now < state.PhaseEndsAt ? now : state.PhaseEndsAt;
			bool scored = false;
			foreach (ArenaObjectiveState objective in state.Objectives.Values)
			{
				if (objective.Kind != ArenaObjectiveKind.ControlPoint || objective.Team < 0 || objective.Team >= state.TeamCount)
				{
					continue;
				}

				// Owned without a capture stamp cannot happen today; were it to, it starts accruing now rather than from the clock's origin.
				if (double.IsNaN(objective.HeldAccruedTo))
				{
					objective.HeldAccruedTo = accrueTo;
				}
				double elapsed = accrueTo - objective.HeldAccruedTo;
				if (accrueTo > objective.HeldAccruedTo)
				{
					objective.HeldAccruedTo = accrueTo;
				}
				objective.HeldSeconds = ArenaRules.AccrueHold(objective.HeldSeconds, elapsed, perPoint, out int points);
				if (points > 0)
				{
					state.TeamScores[objective.Team] += points;
					scored = true;
					AnnounceNearLimit(state, objective.Team);
				}
			}
			return scored;
		}

		/// <summary>
		/// Drops the flag a seat carried where they stood. The flag lies there until its own team
		/// touches it home, an enemy picks it up, or the template's timer returns it.
		/// </summary>
		private void DropCarriedFlag(ArenaMatchState state, ArenaSeatState seat, Vector3 where)
		{
			if (seat.CarriedFlagObjectiveID == 0)
			{
				return;
			}
			if (state.Objectives.TryGetValue(seat.CarriedFlagObjectiveID, out ArenaObjectiveState objective))
			{
				objective.Flag = ArenaFlagState.Dropped;
				objective.CarrierCharacterID = 0;
				objective.DropPosition = where;
				int seconds = state.Template != null ? Math.Max(1, state.Template.FlagDropSeconds) : 20;
				objective.DropExpiresAt = MonotonicClock.NowSeconds + seconds;
				BroadcastArenaEvent(state, ArenaEventKind.FlagDropped, seat, null, objective.Team, seconds);
			}
			seat.CarriedFlagObjectiveID = 0;
		}

		/// <summary>Dropped flags: returned on the timer, or by whoever walks up to them. Returns true when any changed.</summary>
		private bool TickDroppedFlags(ArenaMatchState state, double now, ICharacterMappingData<NetworkConnection> charMapping)
		{
			if (state.Mode != ArenaMode.CaptureTheFlag)
			{
				return false;
			}

			bool changed = false;
			float radius = state.Template != null ? Math.Max(0.5f, state.Template.FlagPickupRadius) : 2.0f;
			float radiusSqr = radius * radius;

			foreach (ArenaObjectiveState objective in state.Objectives.Values)
			{
				if (objective.Kind != ArenaObjectiveKind.FlagStand || objective.Flag != ArenaFlagState.Dropped)
				{
					continue;
				}

				if (now >= objective.DropExpiresAt)
				{
					objective.Flag = ArenaFlagState.Home;
					objective.DropPosition = Vector3.zero;
					changed = true;
					BroadcastArenaEvent(state, ArenaEventKind.FlagReturned, null, null, objective.Team, 0);
					continue;
				}

				if (charMapping == null)
				{
					continue;
				}

				foreach (ArenaSeatState seat in state.Seats.Values)
				{
					if (!seat.Present || seat.Dropped || seat.Forfeited ||
						!charMapping.CharactersByID.TryGetValue(seat.CharacterID, out IPlayerCharacter character) ||
						character?.Transform == null || character.GameObject.scene.handle != state.SceneHandle ||
						character.IsFlagged(CharacterFlags.IsDead) ||
						(character.Transform.position - objective.DropPosition).sqrMagnitude > radiusSqr)
					{
						continue;
					}

					ArenaFlagAction action = ArenaRules.ResolveDroppedFlagTouch(objective.Team, seat.Team, seat.CarriedFlagObjectiveID != 0);
					if (action == ArenaFlagAction.Return)
					{
						objective.Flag = ArenaFlagState.Home;
						objective.DropPosition = Vector3.zero;
						seat.Name = character.CharacterName;
						BroadcastArenaEvent(state, ArenaEventKind.FlagReturned, seat, null, objective.Team, 0);
						changed = true;
						break;
					}
					if (action == ArenaFlagAction.PickUp)
					{
						objective.Flag = ArenaFlagState.Carried;
						objective.CarrierCharacterID = seat.CharacterID;
						objective.DropPosition = Vector3.zero;
						seat.CarriedFlagObjectiveID = objective.ObjectiveID;
						seat.Name = character.CharacterName;
						BroadcastArenaEvent(state, ArenaEventKind.FlagTaken, seat, null, objective.Team, 0);
						changed = true;
						break;
					}
				}
			}
			return changed;
		}

		// ──────────────────────────────────────────────────────────────────
		//  Announcer
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Fires the near-limit announcement for a team that just scored, once.</summary>
		private void AnnounceNearLimit(ArenaMatchState state, int team)
		{
			if (state.Template == null || state.Template.ScoreLimit <= 0 || state.Template.NearScoreLimitPoints <= 0 ||
				team < 0 || team >= state.TeamCount || state.NearLimitFired[team])
			{
				return;
			}
			int left = state.Template.ScoreLimit - state.TeamScores[team];
			if (left > 0 && left <= state.Template.NearScoreLimitPoints)
			{
				state.NearLimitFired[team] = true;
				BroadcastArenaEvent(state, ArenaEventKind.NearScoreLimit, null, null, team, left);
			}
		}

		/// <summary>Sends one announceable moment to everyone standing in the arena.</summary>
		/// <remarks>
		/// One multicast to the instance's scene, serialised once. It used to walk every character
		/// on the server, asking each for its scene with a native call, and serialise the message
		/// again per occupant — on every kill, capture and announcement.
		/// </remarks>
		private void BroadcastArenaEvent(ArenaMatchState state, ArenaEventKind kind, ArenaSeatState actor, ArenaSeatState target, int team, int value)
		{
			var msg = new ArenaEventBroadcast
			{
				Kind = kind,
				ActorID = actor?.CharacterID ?? 0,
				ActorName = actor?.Name ?? string.Empty,
				ActorTeam = actor?.Team ?? -1,
				TargetID = target?.CharacterID ?? 0,
				TargetName = target?.Name ?? string.Empty,
				TargetTeam = target?.Team ?? -1,
				Team = team,
				Value = value,
			};

			Server.NetworkWrapper.BroadcastToScene(state.Scene, msg, true, FishNet.Transporting.Channel.Reliable);
		}

		// ──────────────────────────────────────────────────────────────────
		//  Queue locks
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Locks a character out of the arena queue for some minutes. 0 minutes does nothing.</summary>
		private void LockArenaQueue(long characterID, int minutes, string reason)
		{
			if (minutes <= 0 || characterID <= 0)
			{
				return;
			}
			TimeSpan duration = TimeSpan.FromMinutes(minutes);
			EnqueuePersistence(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IArenaPenaltyService>(out var penaltyService))
					{
						await Log.Warning("InteractableSystem", $"Arena: IArenaPenaltyService unavailable; character {characterID} was not locked out of the queue ({reason}).");
						return;
					}

					// A lock that is not written is a penalty that was never applied: the queue reads only the row.
					DatabaseResult<bool> locked = await WriteArenaRowAsync(() => penaltyService.SetAsync(characterID, duration, reason));
					if (!locked.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"Arena: character {characterID} was not locked out of the queue for {minutes} minutes ({reason}): [{locked.ErrorCode}] {locked.ErrorMessage}");
					}
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error locking character {characterID} out of the arena queue: {ex}");
				}
			}, characterID);
		}

		private void UnlockArenaQueue(long characterID)
		{
			if (characterID <= 0)
			{
				return;
			}
			EnqueuePersistence(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IArenaPenaltyService>(out var penaltyService))
					{
						await Log.Warning("InteractableSystem", $"Arena: IArenaPenaltyService unavailable; character {characterID}'s queue lock was not lifted.");
						return;
					}

					// Left in place, a player who came back inside the grace is still locked out of the queue.
					DatabaseResult<bool> cleared = await WriteArenaRowAsync(() => penaltyService.ClearAsync(characterID));
					if (!cleared.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"Arena: character {characterID}'s queue lock was not lifted: [{cleared.ErrorCode}] {cleared.ErrorMessage}");
					}
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error unlocking character {characterID}'s arena queue: {ex}");
				}
			}, characterID);
		}

		// ──────────────────────────────────────────────────────────────────
		//  Spectators
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// <c>/spectateteam &lt;n&gt;</c>. Moves a spectating game master to team n's spawn in the
		/// match they are watching. Seated players are refused: their team is theirs.
		/// </summary>
		private bool OnSpectateTeamCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character?.Owner == null || character.GameObject == null)
			{
				return true;
			}

			NetworkConnection conn = character.Owner;
			if (!TryGetArenaMatchForScene(character.GameObject.scene.handle, out ArenaMatchState state))
			{
				SendSystemMessage(conn, "You are not in an arena match.");
				return true;
			}

			if (state.Seats.ContainsKey(character.ID))
			{
				SendSystemMessage(conn, "You are playing in this match, not spectating it.");
				return true;
			}

			string text = ChatHelper.GetWordAndTrimmed(msg.Text, out _);
			if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int team) || team < 1 || team > state.TeamCount)
			{
				SendSystemMessage(conn, $"Usage: /spectateteam <1-{state.TeamCount}>");
				return true;
			}

			MoveToTeamSpawn(state, character, team - 1);
			SendSystemMessage(conn, $"Watching from team {team}'s side.");
			return true;
		}

		// ──────────────────────────────────────────────────────────────────
		//  Helpers
		// ──────────────────────────────────────────────────────────────────

		private static int CountTeamsWithPlayers(ArenaMatchState state)
		{
			int count = 0;
			for (int t = 0; t < state.TeamCount; ++t)
			{
				if (TeamHasPlayers(state, t))
				{
					++count;
				}
			}
			return count;
		}

		/// <summary>A team has players while any seat is present, or disconnected inside its grace.</summary>
		private static bool TeamHasPlayers(ArenaMatchState state, int team)
		{
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Team == team && !seat.Dropped && !seat.Forfeited && (seat.Present || seat.DisconnectedAt.HasValue))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Publishes the roster to the team registry, without dropped seats.</summary>
		private void PublishArenaRoster(ArenaMatchState state)
		{
			var roster = new Dictionary<long, int>(state.Seats.Count);
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (!seat.Dropped)
				{
					roster[seat.CharacterID] = seat.Team;
				}
			}
			ArenaTeamRegistry.Publish(state.SceneHandle, roster, state.Phase == ArenaMatchPhase.Live, state.Template?.ResolveTeamColors());
		}

		/// <summary>Revives a character at full health and moves them to their team's spawn.</summary>
		private void RespawnInArena(ArenaMatchState state, IPlayerCharacter character, int team)
		{
			if (character == null)
			{
				return;
			}

			if (character.IsFlagged(CharacterFlags.IsDead))
			{
				character.DisableFlags(CharacterFlags.IsDead);
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					int amount = int.MaxValue;
					if (character.TryGet(out ICharacterAttributeController attributes) &&
						attributes.TryGetHealthAttribute(out CharacterResourceAttribute health))
					{
						amount = Math.Max(1, health.FinalValue);
					}
					damageController.Revive(character, amount);
				}
			}

			MoveToTeamSpawn(state, character, team);
		}

		/// <summary>Moves a character to one of their team's spawn points.</summary>
		private void MoveToTeamSpawn(ArenaMatchState state, IPlayerCharacter character, int team)
		{
			if (character?.Motor == null || worldSceneDetailsCache == null ||
				!worldSceneDetailsCache.Scenes.TryGetValue(state.SceneName, out WorldSceneDetails details) ||
				details.RespawnPositions == null || details.RespawnPositions.Count < 1)
			{
				return;
			}

			string prefix = state.Template != null ? state.Template.GetTeamSpawnPrefix(team) : null;
			List<string> keys = ArenaRules.ResolveTeamSpawnKeys(details.RespawnPositions.Keys, prefix);
			if (keys.Count == 0)
			{
				return;
			}

			string key = keys[UnityEngine.Random.Range(0, keys.Count)];
			if (details.RespawnPositions.TryGetValue(key, out CharacterRespawnPositionDetails spawn) && spawn != null)
			{
				character.Motor.SetPositionAndRotationAndVelocity(spawn.Position, spawn.Rotation, Vector3.zero);
			}
		}

		/// <summary>Records a result on a character's PvP attributes and returns the rank change.</summary>
		private int ApplyPvPResult(IPlayerCharacter character, int team, int winnerTeam, int winPoints, int lossPoints)
		{
			int currentRank = 0;
			if (TryGetPvPAttribute(character, PvPRankAttributeName, out CharacterAttribute rank))
			{
				currentRank = rank.Value;
			}

			int delta = ArenaRules.ResolveRankDelta(currentRank, team, winnerTeam, winPoints, lossPoints);

			AdjustPvPAttribute(character, PvPRankAttributeName, delta);
			AdjustPvPAttribute(character, PvPMatchesAttributeName, 1);
			if (winnerTeam >= 0)
			{
				AdjustPvPAttribute(character, team == winnerTeam ? PvPWinsAttributeName : PvPLossesAttributeName, 1);
			}
			return delta;
		}

		private bool TryGetPvPAttribute(IPlayerCharacter character, string templateName, out CharacterAttribute attribute)
		{
			attribute = null;
			if (!TryResolvePvPTemplate(templateName, out CharacterAttributeTemplate template) ||
				!character.TryGet(out ICharacterAttributeController attributes))
			{
				return false;
			}
			return attributes.TryGetAttribute(template, out attribute) && attribute != null;
		}

		/// <summary>
		/// Adds to a PvP attribute so that the change persists.
		/// </summary>
		/// <remarks>
		/// An attribute a character has never had saved sits at version 0, and the save path skips
		/// version-0 attributes as template defaults. Every character created before these
		/// attributes existed is in that state, so the version is seeded to 1 on the first change:
		/// the save then bumps it to 2 and the upsert inserts the row. Once a row exists the
		/// ordinary dirty tracking takes over.
		/// </remarks>
		private void AdjustPvPAttribute(IPlayerCharacter character, string templateName, int delta)
		{
			if (delta == 0 || !TryGetPvPAttribute(character, templateName, out CharacterAttribute attribute))
			{
				return;
			}

			if (attribute.Version <= 0)
			{
				attribute.Version = 1;
			}
			attribute.AddValue(delta);
		}

		private bool TryResolvePvPTemplate(string templateName, out CharacterAttributeTemplate template)
		{
			if (pvpAttributeTemplates.TryGetValue(templateName, out template) && template != null)
			{
				return true;
			}

			Dictionary<int, CharacterAttributeTemplate> cache = CharacterAttributeTemplate.GetCache<CharacterAttributeTemplate>();
			if (cache != null)
			{
				foreach (CharacterAttributeTemplate candidate in cache.Values)
				{
					if (candidate != null && string.Equals(candidate.Name, templateName, StringComparison.Ordinal))
					{
						pvpAttributeTemplates[templateName] = candidate;
						template = candidate;
						return true;
					}
				}
			}

			if (pvpAttributeWarnings.Add(templateName))
			{
				Log.Warning("InteractableSystem", $"Arena: no CharacterAttributeTemplate named '{templateName}' is loaded; PvP results will not be recorded on it.");
			}
			template = null;
			return false;
		}

		private void PersistArenaStatus(ArenaMatchState state, ArenaMatchStatus status)
		{
			long matchID = state.MatchID;
			EnqueuePersistence(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
					{
						await Log.Error("InteractableSystem", $"Arena: IArenaMatchService unavailable; match {matchID} was not moved to {status}.");
						return;
					}
					await WriteArenaStatusAsync(matchService, matchID, status, -1);
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error updating arena match {matchID} to {status}: {ex}");
				}
			}, matchID);
		}

		/// <summary>
		/// Moves a match's row to a status, retrying a failed write, and logs a failure that stuck.
		/// </summary>
		/// <remarks>
		/// Worth the retry because a match row that never reaches Ended or Cancelled holds every one
		/// of its seats "in a live match": both finders refuse them until the stale sweep's
		/// <c>CancelAbandonedAsync</c> reaps the row, which waits for the instance to be gone and
		/// the match to be ten minutes old. Safe to repeat — the status only ever moves forward, so
		/// a write that did land and is sent again moves nothing.
		/// </remarks>
		private async Task WriteArenaStatusAsync(IArenaMatchService matchService, long matchID, ArenaMatchStatus status, int winnerTeam)
		{
			DatabaseResult<bool> result = await WriteArenaRowAsync(() => matchService.UpdateStatusAsync(matchID, status, winnerTeam));
			if (!result.IsSuccess)
			{
				await Log.Error("InteractableSystem", $"Arena: match {matchID} could not be moved to {status}; its seats stay blocked from both finders until the stale sweep cancels it: [{result.ErrorCode}] {result.ErrorMessage}");
			}
		}

		/// <summary>
		/// Runs one idempotent arena write, retrying a failure up to <see cref="ArenaWriteAttempts"/>
		/// times with a short, growing backoff.
		/// </summary>
		/// <remarks>
		/// The service layer already retries transient errors inside a single call; this spans the
		/// few seconds a database restart or failover takes, which that does not. Only for writes
		/// that are safe to repeat when an earlier attempt's outcome is unknown — status moves,
		/// seat and window stamps, lock upserts, per-seat SETs. Never the rating upsert, which
		/// counts the game.
		/// </remarks>
		/// <returns>The last attempt's result.</returns>
		private static async Task<DatabaseResult<T>> WriteArenaRowAsync<T>(Func<Task<DatabaseResult<T>>> write)
		{
			DatabaseResult<T> result = default;
			for (int attempt = 1; attempt <= ArenaWriteAttempts; ++attempt)
			{
				result = await write();
				// A request the service refused as invalid will be refused the same way every time.
				if (result.IsSuccess || result.ErrorCode == DatabaseErrorCodes.ValidationError)
				{
					return result;
				}
				if (attempt < ArenaWriteAttempts)
				{
					await Task.Delay(ArenaWriteRetryDelayMilliseconds * attempt);
				}
			}
			return result;
		}

		/// <summary>Sends the match state to everyone standing in the arena.</summary>
		/// <remarks>
		/// One multicast to the instance's scene; see <see cref="BroadcastArenaEvent"/>. A
		/// connection still loading into the scene is among its connections and receives this too;
		/// the client ignores match state until its character exists.
		/// </remarks>
		private void BroadcastArenaState(ArenaMatchState state, int secondsRemaining = 0)
		{
			// Nobody in the scene — the last player just left — is nobody to build the scoreboard for.
			if (!Server.NetworkWrapper.TryGetSceneConnections(state.Scene, out HashSet<NetworkConnection> occupants))
			{
				return;
			}

			var entries = new List<ArenaMemberEntry>(state.Seats.Count);
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Dropped || seat.Forfeited)
				{
					continue;
				}
				entries.Add(new ArenaMemberEntry
				{
					CharacterID = seat.CharacterID,
					Team = seat.Team,
					Kills = seat.Kills,
					Deaths = seat.Deaths,
					Score = seat.Score,
					Present = seat.Present,
					Ready = seat.Ready,
					Reconnecting = seat.DisconnectedAt.HasValue,
				});
			}

			var objectives = new ArenaObjectiveEntry[state.Objectives.Count];
			int o = 0;
			foreach (ArenaObjectiveState objective in state.Objectives.Values)
			{
				objectives[o++] = objective.Kind == ArenaObjectiveKind.FlagStand
					? new ArenaObjectiveEntry { ObjectiveID = objective.ObjectiveID, Kind = objective.Kind, Team = objective.Team, Progress = (int)objective.Flag, Holder = objective.CarrierCharacterID, Position = objective.Flag == ArenaFlagState.Dropped ? objective.DropPosition : Vector3.zero }
					: new ArenaObjectiveEntry { ObjectiveID = objective.ObjectiveID, Kind = objective.Kind, Team = objective.Team, Progress = objective.Progress, Holder = objective.ProgressTeam };
			}

			var msg = new ArenaMatchStateBroadcast
			{
				ArenaTemplateID = state.Template != null ? state.Template.ID : 0,
				Format = state.Format,
				Phase = state.Phase,
				SecondsRemaining = secondsRemaining,
				TeamScores = (int[])state.TeamScores.Clone(),
				Members = entries.ToArray(),
				Objectives = objectives,
			};

			/* Everyone standing in the arena, not only the seats: a spectating game master sees the
			 * same scoreboard, and a seat that arrived a moment ago is covered either way. Read and
			 * sent in this one call, on the main thread, as FishNet's set requires. */
			Server.NetworkWrapper.Broadcast(occupants, msg, true, FishNet.Transporting.Channel.Reliable);
		}
	}
}
