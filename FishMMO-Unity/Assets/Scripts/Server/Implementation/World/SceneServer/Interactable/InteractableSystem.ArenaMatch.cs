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
	/// timeout, after which absentees are dropped and the match starts if two teams still have
	/// players, or is cancelled. <em>Countdown</em> moves everyone to their team's spawn and counts
	/// down the template's seconds, broadcasting each one so clients can fire their cues; nobody
	/// can be hurt yet, because <see cref="ArenaTeamRegistry"/> reports every seat as an ally until
	/// the match is live. <em>Live</em> scores kills, respawns the dead at their team's spawn after
	/// the template's delay, and ends on the score limit, the clock, or a walkover.
	/// <em>Ended</em> writes the tallies and the result, adjusts every present player's PvP
	/// attributes, shows the results screen for the template's seconds, and then closes the
	/// instance through the same path a dungeon closes by.
	/// </para>
	/// <para>
	/// Team Deathmatch scores kills. Capture the Flag scores a carried enemy flag delivered to the
	/// carrier's own stand while their own flag is home; a carrier's death or departure returns the
	/// flag to its stand. King of the Hill scores a point for a control point's owner every
	/// <c>ControlPointHoldSecondsPerPoint</c> seconds; the point changes hands after
	/// <c>ControlPointCaptureInteractions</c> touches by one team. All three share every phase.
	/// </para>
	/// </remarks>
	public partial class InteractableSystem
	{
		/// <summary>Seconds between arena ticks. One, because the countdown is announced per second.</summary>
		private const float ArenaTickSeconds = 1.0f;

		/// <summary>How long a cancelled match's occupants see the notice before being returned.</summary>
		private const int ArenaCancelledSeconds = 5;

		/// <summary>Attribute template names the arena adjusts. Authored as CharacterAttributeTemplate assets.</summary>
		private const string PvPRankAttributeName = "PvP Rank";
		private const string PvPWinsAttributeName = "PvP Wins";
		private const string PvPLossesAttributeName = "PvP Losses";
		private const string PvPMatchesAttributeName = "PvP Matches";

		/// <summary>One seat as this server tracks it.</summary>
		private sealed class ArenaSeatState
		{
			public long CharacterID;
			public int Team;
			public int Kills;
			public int Deaths;
			public int Score;
			/// <summary>Standing in the instance right now.</summary>
			public bool Present;
			/// <summary>Never arrived before the gathering timeout; no longer part of the match.</summary>
			public bool Dropped;
			/// <summary>When a dead player is put back, or null while alive.</summary>
			public DateTime? RespawnAtUtc;
			/// <summary>Left the match while it was live. Their loss was recorded as they left; nothing more is written for them.</summary>
			public bool Forfeited;
			/// <summary>Scene object id of the enemy flag stand whose flag they carry, or 0.</summary>
			public long CarriedFlagObjectiveID;
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
			/// <summary>Control point: team whose capture is in progress, or -1.</summary>
			public int ProgressTeam = -1;
			/// <summary>Control point: interactions towards a capture.</summary>
			public int Progress;
			/// <summary>Control point: seconds held since the last point was scored.</summary>
			public int HeldSeconds;
		}

		/// <summary>One match hosted here.</summary>
		private sealed class ArenaMatchState
		{
			public long MatchID;
			public long InstanceID;
			public int SceneHandle;
			public string SceneName;
			public ArenaTemplate Template;
			public int Format;
			public int TeamCount;
			public int TeamSize;
			public ArenaMatchPhase Phase;
			public DateTime PhaseEndsUtc;
			public int LastBroadcastSecond = -1;
			public int[] TeamScores;
			public int WinnerTeam = -1;
			public readonly Dictionary<long, ArenaSeatState> Seats = new Dictionary<long, ArenaSeatState>();
			/// <summary>Objectives in the scene, by scene object id. Empty for deathmatch.</summary>
			public readonly Dictionary<long, ArenaObjectiveState> Objectives = new Dictionary<long, ArenaObjectiveState>();

			public ArenaMode Mode => Template != null ? Template.Mode : ArenaMode.TeamDeathmatch;
		}

		/// <summary>Matches hosted here, by instance row id. Main thread only.</summary>
		private readonly Dictionary<long, ArenaMatchState> arenaMatchesByInstance = new Dictionary<long, ArenaMatchState>();

		/// <summary>Instance row id by Unity scene handle, for the events that only know the scene.</summary>
		private readonly Dictionary<int, long> arenaInstanceBySceneHandle = new Dictionary<int, long>();

		/// <summary>Instances whose match rows are being read.</summary>
		private readonly HashSet<long> arenaMatchesLoading = new HashSet<long>();

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

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnArenaTick);
			}

			arenaMatchesByInstance.Clear();
			arenaInstanceBySceneHandle.Clear();
			arenaMatchesLoading.Clear();
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

			if (!arenaMatchesLoading.Add(details.SceneID))
			{
				// Already being read; the arrival is picked up from the scene when it lands.
				return;
			}

			long instanceID = details.SceneID;
			int sceneHandle = scene.handle;
			string sceneName = details.Name;

			if (!TryEnqueueAsyncWork(() => LoadArenaMatchAsync(instanceID, sceneHandle, sceneName), instanceID))
			{
				arenaMatchesLoading.Remove(instanceID);
				Log.Warning("InteractableSystem", $"Arena: could not enqueue the match read for instance {instanceID}.");
			}
		}

		/// <summary>Reads the match and its seats for an instance this server has just started hosting.</summary>
		private async Task LoadArenaMatchAsync(long instanceID, int sceneHandle, string sceneName)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
				{
					TryEnqueueMainThread(() => arenaMatchesLoading.Remove(instanceID));
					return;
				}

				DatabaseResult<ArenaMatchData?> matchResult = await matchService.FetchByInstanceAsync(instanceID);
				if (!matchResult.IsSuccess || !matchResult.Data.HasValue)
				{
					await Log.Warning("InteractableSystem", $"Arena: instance {instanceID} ('{sceneName}') is a PvP scene with no match row; it will run as a plain instance.");
					TryEnqueueMainThread(() => arenaMatchesLoading.Remove(instanceID));
					return;
				}

				ArenaMatchData match = matchResult.Data.Value;
				DatabaseResult<IReadOnlyList<ArenaMatchMemberData>> membersResult = await matchService.FetchMembersAsync(match.ID);
				if (!membersResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"Arena: could not read the seats of match {match.ID}: {membersResult.ErrorCode} - {membersResult.ErrorMessage}");
					TryEnqueueMainThread(() => arenaMatchesLoading.Remove(instanceID));
					return;
				}

				IReadOnlyList<ArenaMatchMemberData> members = membersResult.Data;
				TryEnqueueMainThread(() => RegisterArenaMatch(match, members, sceneHandle));
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error reading arena match for instance {instanceID}: {ex}");
				TryEnqueueMainThread(() => arenaMatchesLoading.Remove(instanceID));
			}
		}

		/// <summary>Creates the local match state and seats everyone already standing in the scene. Main thread only.</summary>
		private void RegisterArenaMatch(ArenaMatchData match, IReadOnlyList<ArenaMatchMemberData> members, int sceneHandle)
		{
			arenaMatchesLoading.Remove(match.InstanceID);

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
				SceneHandle = sceneHandle,
				SceneName = match.SceneName,
				Template = template,
				Format = match.Format,
				TeamCount = Math.Max(2, match.TeamCount),
				TeamSize = Math.Max(1, match.TeamSize),
				Phase = ArenaMatchPhase.Gathering,
				PhaseEndsUtc = DateTime.UtcNow.AddSeconds(template != null ? template.GatheringTimeoutSeconds : 90),
			};
			state.TeamScores = new int[state.TeamCount];

			foreach (ArenaMatchMemberData member in members)
			{
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

			// Whoever arrived while the rows were being read.
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				foreach (IPlayerCharacter resident in charMapping.CharactersByID.Values)
				{
					if (resident?.GameObject != null && resident.GameObject.scene.handle == sceneHandle)
					{
						SeatArrived(state, resident);
					}
				}
			}

			if (template == null)
			{
				CancelArenaMatch(state, "its arena template could not be resolved");
				return;
			}

			Log.Debug("InteractableSystem", $"Arena: hosting match {state.MatchID} ('{template.name}' {template.GetFormatName(state.Format)}) in instance {state.InstanceID}; {state.Seats.Count} seats.");
			BroadcastArenaState(state);
		}

		/// <summary>Marks a seat present. A stranger with no seat is left alone; the registry treats them as an ally to all.</summary>
		private void SeatArrived(ArenaMatchState state, IPlayerCharacter character)
		{
			if (!state.Seats.TryGetValue(character.ID, out ArenaSeatState seat))
			{
				Log.Debug("InteractableSystem", $"Arena: {character.CharacterName} entered match {state.MatchID}'s instance without a seat.");
				return;
			}

			seat.Present = true;

			/* Arriving during the countdown after having been dropped: reseat them. Arriving once
			 * live stays dropped — the sides were settled when play began. */
			if (seat.Dropped && state.Phase <= ArenaMatchPhase.Countdown)
			{
				seat.Dropped = false;
				PublishArenaRoster(state);
			}

			if (state.Phase == ArenaMatchPhase.Countdown || state.Phase == ArenaMatchPhase.Live)
			{
				MoveToTeamSpawn(state, character, seat.Team);
			}

			BroadcastArenaState(state);
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
			seat.RespawnAtUtc = null;
			ReturnCarriedFlag(state, seat);

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
		/// is written to their attributes now, while they are still in memory and about to be saved.
		/// </summary>
		/// <remarks>
		/// Fires for the match's own eviction too, but by then the phase is Ended and nothing here
		/// applies. A quit during gathering or the countdown is not a forfeit — no match was played.
		/// A seat that forfeits is skipped by the end-of-match stats, so a leaver is never charged
		/// twice, and a leaver who was still counted as present is marked absent so a walkover can
		/// be decided immediately rather than on the despawn that follows.
		/// </remarks>
		private void CharacterSystem_OnArenaCharacterLeft(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character?.GameObject == null ||
				!TryGetArenaMatchForScene(character.GameObject.scene.handle, out ArenaMatchState state) ||
				!state.Seats.TryGetValue(character.ID, out ArenaSeatState seat) ||
				seat.Dropped || seat.Forfeited)
			{
				return;
			}

			if (state.Phase != ArenaMatchPhase.Live)
			{
				return;
			}

			seat.Forfeited = true;
			seat.Present = false;
			seat.RespawnAtUtc = null;
			ReturnCarriedFlag(state, seat);

			int lossPoints = state.Template != null ? state.Template.LossRankPoints : 5;
			int winPoints = state.Template != null ? state.Template.WinRankPoints : 10;
			// A forfeit is a loss to whichever team is not theirs; any other team index will do.
			int notTheirTeam = seat.Team == 0 ? 1 : 0;
			ApplyPvPResult(character, seat.Team, notTheirTeam, winPoints, lossPoints);

			Log.Debug("InteractableSystem", $"Arena: {character.CharacterName} left match {state.MatchID} while it was live and forfeited.");

			if (!CheckArenaOutcome(state, timeUp: false))
			{
				BroadcastArenaState(state);
			}
		}

		private bool TryGetArenaMatchForScene(int sceneHandle, out ArenaMatchState state)
		{
			state = null;
			return arenaInstanceBySceneHandle.TryGetValue(sceneHandle, out long instanceID) &&
				arenaMatchesByInstance.TryGetValue(instanceID, out state);
		}

		// ──────────────────────────────────────────────────────────────────
		//  Kills
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Scores a kill inside a live match and schedules the victim's respawn.</summary>
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
			ReturnCarriedFlag(state, victimSeat);

			if (killer is IPlayerCharacter attacker &&
				state.Seats.TryGetValue(attacker.ID, out ArenaSeatState killerSeat) &&
				!killerSeat.Dropped &&
				killerSeat.Team != victimSeat.Team)
			{
				killerSeat.Kills += 1;
				if (state.Template == null || state.Template.Mode == ArenaMode.TeamDeathmatch)
				{
					killerSeat.Score += 1;
					state.TeamScores[killerSeat.Team] += 1;
				}
			}

			int respawnSeconds = state.Template != null ? state.Template.RespawnSeconds : 0;
			victimSeat.RespawnAtUtc = respawnSeconds > 0 ? DateTime.UtcNow.AddSeconds(respawnSeconds) : (DateTime?)null;

			if (victim.Owner != null && victim.Owner.IsActive)
			{
				Server.NetworkWrapper.Broadcast(victim.Owner, new ArenaRespawnBroadcast { SecondsUntilRespawn = respawnSeconds }, true, FishNet.Transporting.Channel.Reliable);
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
		private void OnArenaTick(float deltaTime)
		{
			if (arenaMatchesByInstance.Count == 0 || Server == null)
			{
				return;
			}

			DateTime now = DateTime.UtcNow;
			List<ArenaMatchState> finished = null;

			foreach (ArenaMatchState state in arenaMatchesByInstance.Values.ToList())
			{
				/* The instance went away under us: a lifetime cap, a close from elsewhere. The match
				 * row must not stay open, or every seat is "in a live match" forever and locked out
				 * of both finders; a match that had not ended is recorded as cancelled. */
				if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) ||
					!mappingData.SceneInstanceByHandle.ContainsKey(state.SceneHandle))
				{
					if (state.Phase < ArenaMatchPhase.Ended)
					{
						Log.Warning("InteractableSystem", $"Arena: match {state.MatchID}'s instance disappeared while {state.Phase}; recording it as cancelled.");
						PersistArenaStatus(state, ArenaMatchStatus.Cancelled);
					}
					(finished ??= new List<ArenaMatchState>()).Add(state);
					continue;
				}

				switch (state.Phase)
				{
					case ArenaMatchPhase.Gathering:
						TickGathering(state, now);
						break;
					case ArenaMatchPhase.Countdown:
						TickCountdown(state, now);
						break;
					case ArenaMatchPhase.Live:
						TickLive(state, now);
						break;
					case ArenaMatchPhase.Ended:
					case ArenaMatchPhase.Cancelled:
						if (now >= state.PhaseEndsUtc)
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

		private void TickGathering(ArenaMatchState state, DateTime now)
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
				BeginArenaCountdown(state);
				return;
			}

			if (now < state.PhaseEndsUtc)
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

			BeginArenaCountdown(state);
		}

		private void TickCountdown(ArenaMatchState state, DateTime now)
		{
			int seconds = Math.Max(0, (int)Math.Ceiling((state.PhaseEndsUtc - now).TotalSeconds));
			if (seconds != state.LastBroadcastSecond)
			{
				state.LastBroadcastSecond = seconds;
				BroadcastArenaState(state, seconds);
			}

			if (seconds <= 0)
			{
				GoLive(state);
			}
		}

		private void TickLive(ArenaMatchState state, DateTime now)
		{
			// Respawns due.
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				foreach (ArenaSeatState seat in state.Seats.Values)
				{
					if (!seat.RespawnAtUtc.HasValue || now < seat.RespawnAtUtc.Value || !seat.Present)
					{
						continue;
					}

					seat.RespawnAtUtc = null;
					if (charMapping.CharactersByID.TryGetValue(seat.CharacterID, out IPlayerCharacter character) &&
						character?.GameObject != null && character.GameObject.scene.handle == state.SceneHandle &&
						character.IsFlagged(CharacterFlags.IsDead))
					{
						// Still dead. A teammate's resurrection in the meantime leaves them where they stand.
						RespawnInArena(state, character, seat.Team);
					}
				}
			}

			bool scored = TickControlPoints(state);

			bool timed = state.Template != null && state.Template.MatchMinutes > 0;
			int seconds = timed ? Math.Max(0, (int)Math.Ceiling((state.PhaseEndsUtc - now).TotalSeconds)) : 0;
			bool timeUp = timed && seconds <= 0;

			if (CheckArenaOutcome(state, timeUp))
			{
				return;
			}

			if (scored || (timed && seconds != state.LastBroadcastSecond))
			{
				state.LastBroadcastSecond = seconds;
				BroadcastArenaState(state, seconds);
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Phase changes
		// ──────────────────────────────────────────────────────────────────

		private void BeginArenaCountdown(ArenaMatchState state)
		{
			state.Phase = ArenaMatchPhase.Countdown;
			int seconds = state.Template != null ? Math.Max(1, state.Template.CountdownSeconds) : 10;
			state.PhaseEndsUtc = DateTime.UtcNow.AddSeconds(seconds);
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
			state.PhaseEndsUtc = timed ? DateTime.UtcNow.AddMinutes(state.Template.MatchMinutes) : DateTime.MaxValue;
			state.LastBroadcastSecond = -1;
			ArenaTeamRegistry.SetLive(state.SceneHandle, true);

			PersistArenaStatus(state, ArenaMatchStatus.Live);
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
			state.PhaseEndsUtc = DateTime.UtcNow.AddSeconds(resultsSeconds);
			ArenaTeamRegistry.SetLive(state.SceneHandle, false);

			Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} ended; winner team {winnerTeam}; scores {string.Join("/", state.TeamScores)}.");

			// Placements by score only.
			var lines = new List<ArenaPlacement>(state.Seats.Count);
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Dropped)
				{
					continue;
				}
				lines.Add(new ArenaPlacement { CharacterID = seat.CharacterID, Team = seat.Team, Kills = seat.Kills, Deaths = seat.Deaths, Score = seat.Score });
			}
			List<ArenaPlacement> placements = ArenaRules.ResolvePlacements(lines);

			var placementEntries = new ArenaMemberEntry[placements.Count];
			for (int i = 0; i < placements.Count; ++i)
			{
				ArenaPlacement p = placements[i];
				placementEntries[i] = new ArenaMemberEntry { CharacterID = p.CharacterID, Team = p.Team, Kills = p.Kills, Deaths = p.Deaths, Score = p.Score, Present = state.Seats[p.CharacterID].Present };
			}

			// Stats and results, for everyone still here.
			int winPoints = state.Template != null ? state.Template.WinRankPoints : 10;
			int lossPoints = state.Template != null ? state.Template.LossRankPoints : 5;
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				foreach (ArenaSeatState seat in state.Seats.Values)
				{
					if (seat.Dropped || seat.Forfeited || !seat.Present ||
						!charMapping.CharactersByID.TryGetValue(seat.CharacterID, out IPlayerCharacter character) ||
						character?.Owner == null || !character.Owner.IsActive)
					{
						continue;
					}

					int rankDelta = ApplyPvPResult(character, seat.Team, winnerTeam, winPoints, lossPoints);

					Server.NetworkWrapper.Broadcast(character.Owner, new ArenaResultsBroadcast
					{
						MatchID = state.MatchID,
						ArenaTemplateID = state.Template != null ? state.Template.ID : 0,
						Format = state.Format,
						WinnerTeam = winnerTeam,
						TeamScores = (int[])state.TeamScores.Clone(),
						Placements = placementEntries,
						YourTeam = seat.Team,
						RankDelta = rankDelta,
						SecondsUntilReturn = resultsSeconds,
					}, true, FishNet.Transporting.Channel.Reliable);
				}
			}

			BroadcastArenaState(state);

			// Tallies and the result, written together.
			var tallies = new List<(long, int, int, int)>(state.Seats.Count);
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				tallies.Add((seat.CharacterID, seat.Kills, seat.Deaths, seat.Score));
			}
			long matchID = state.MatchID;
			TryEnqueueAsyncWork(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry == null ||
						!Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
					{
						return;
					}
					await matchService.UpdateMemberTalliesAsync(matchID, tallies);
					await matchService.UpdateStatusAsync(matchID, ArenaMatchStatus.Ended, winnerTeam);
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error recording arena match {matchID}: {ex}");
				}
			}, matchID);
		}

		private void CancelArenaMatch(ArenaMatchState state, string reason)
		{
			state.Phase = ArenaMatchPhase.Cancelled;
			state.PhaseEndsUtc = DateTime.UtcNow.AddSeconds(ArenaCancelledSeconds);
			ArenaTeamRegistry.SetLive(state.SceneHandle, false);

			Log.Debug("InteractableSystem", $"Arena: match {state.MatchID} cancelled: {reason}.");
			PersistArenaStatus(state, ArenaMatchStatus.Cancelled);

			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				foreach (ArenaSeatState seat in state.Seats.Values)
				{
					if (seat.Present && charMapping.CharactersByID.TryGetValue(seat.CharacterID, out IPlayerCharacter character) && character?.Owner != null)
					{
						SendSystemMessage(character.Owner, $"The match was cancelled: {reason}. You will be returned to the world.");
					}
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
			arenaInstanceBySceneHandle.Remove(state.SceneHandle);
			ArenaTeamRegistry.Unpublish(state.SceneHandle);
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
				if (objective.Kind == ArenaObjectiveKind.ControlPoint)
				{
					objective.Team = -1;
				}
				objective.ProgressTeam = -1;
				objective.Progress = 0;
				objective.HeldSeconds = 0;
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

			if (tracked.Kind == ArenaObjectiveKind.FlagStand && state.Mode == ArenaMode.CaptureTheFlag)
			{
				switch (ArenaRules.ResolveFlagInteraction(tracked.Team, tracked.Flag, seat.Team, seat.CarriedFlagObjectiveID != 0))
				{
					case ArenaFlagAction.PickUp:
						tracked.Flag = ArenaFlagState.Carried;
						tracked.CarrierCharacterID = player.ID;
						seat.CarriedFlagObjectiveID = tracked.ObjectiveID;
						changed = true;
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
					tracked.HeldSeconds = 0;
					seat.Score += state.Template != null ? state.Template.ControlPointCaptureScore : 5;
					Log.Debug("InteractableSystem", $"Arena: {player.CharacterName} captured a control point for team {seat.Team + 1} in match {state.MatchID}.");
				}
			}

			if (changed && !CheckArenaOutcome(state, timeUp: false))
			{
				BroadcastArenaState(state);
			}
		}

		/// <summary>Scores held control points once a second. Returns true when a team scored.</summary>
		private static bool TickControlPoints(ArenaMatchState state)
		{
			if (state.Mode != ArenaMode.KingOfTheHill)
			{
				return false;
			}

			int perPoint = state.Template != null ? Math.Max(1, state.Template.ControlPointHoldSecondsPerPoint) : 1;
			bool scored = false;
			foreach (ArenaObjectiveState objective in state.Objectives.Values)
			{
				if (objective.Kind != ArenaObjectiveKind.ControlPoint || objective.Team < 0 || objective.Team >= state.TeamCount)
				{
					continue;
				}

				objective.HeldSeconds += 1;
				if (objective.HeldSeconds >= perPoint)
				{
					objective.HeldSeconds = 0;
					state.TeamScores[objective.Team] += 1;
					scored = true;
				}
			}
			return scored;
		}

		/// <summary>Sends a flag its carrier held back to its stand.</summary>
		private static void ReturnCarriedFlag(ArenaMatchState state, ArenaSeatState seat)
		{
			if (seat.CarriedFlagObjectiveID == 0)
			{
				return;
			}
			if (state.Objectives.TryGetValue(seat.CarriedFlagObjectiveID, out ArenaObjectiveState objective))
			{
				objective.Flag = ArenaFlagState.Home;
				objective.CarrierCharacterID = 0;
			}
			seat.CarriedFlagObjectiveID = 0;
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

		private static bool TeamHasPlayers(ArenaMatchState state, int team)
		{
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Team == team && seat.Present && !seat.Dropped)
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
			ArenaTeamRegistry.Publish(state.SceneHandle, roster, state.Phase == ArenaMatchPhase.Live);
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
			TryEnqueueAsyncWork(async () =>
			{
				try
				{
					if (Server?.Database?.ServiceRegistry != null &&
						Server.Database.ServiceRegistry.TryGet<IArenaMatchService>(out var matchService))
					{
						await matchService.UpdateStatusAsync(matchID, status);
					}
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Error updating arena match {matchID} to {status}: {ex}");
				}
			}, matchID);
		}

		/// <summary>Sends the match state to every present seat.</summary>
		private void BroadcastArenaState(ArenaMatchState state, int secondsRemaining = 0)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				return;
			}

			var entries = new List<ArenaMemberEntry>(state.Seats.Count);
			foreach (ArenaSeatState seat in state.Seats.Values)
			{
				if (seat.Dropped)
				{
					continue;
				}
				entries.Add(new ArenaMemberEntry { CharacterID = seat.CharacterID, Team = seat.Team, Kills = seat.Kills, Deaths = seat.Deaths, Score = seat.Score, Present = seat.Present });
			}

			var objectives = new ArenaObjectiveEntry[state.Objectives.Count];
			int o = 0;
			foreach (ArenaObjectiveState objective in state.Objectives.Values)
			{
				objectives[o++] = objective.Kind == ArenaObjectiveKind.FlagStand
					? new ArenaObjectiveEntry { ObjectiveID = objective.ObjectiveID, Kind = objective.Kind, Team = objective.Team, Progress = objective.Flag == ArenaFlagState.Carried ? 1 : 0, Holder = objective.CarrierCharacterID }
					: new ArenaObjectiveEntry { ObjectiveID = objective.ObjectiveID, Kind = objective.Kind, Team = objective.Team, Progress = objective.Progress, Holder = objective.ProgressTeam };
			}

			var msg = new ArenaMatchStateBroadcast
			{
				MatchID = state.MatchID,
				ArenaTemplateID = state.Template != null ? state.Template.ID : 0,
				Format = state.Format,
				Phase = state.Phase,
				SecondsRemaining = secondsRemaining,
				TeamScores = (int[])state.TeamScores.Clone(),
				Members = entries.ToArray(),
				Objectives = objectives,
			};

			/* Everyone standing in the arena, not only the seats: a spectating game master sees the
			 * same scoreboard, and a seat that arrived a moment ago is covered either way. */
			foreach (IPlayerCharacter occupant in charMapping.CharactersByID.Values)
			{
				if (occupant?.GameObject != null && occupant.GameObject.scene.handle == state.SceneHandle &&
					occupant.Owner != null && occupant.Owner.IsActive)
				{
					Server.NetworkWrapper.Broadcast(occupant.Owner, msg, true, FishNet.Transporting.Channel.Reliable);
				}
			}
		}
	}
}
