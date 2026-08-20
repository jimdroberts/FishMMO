using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Operator control of the running servers: locking them to new arrivals and scheduling
	/// maintenance shutdowns, plus the in-game <c>/admin</c> commands that drive both.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The database row is the authority for a server's lock and shutdown state, never the
	/// process. Commands here write the row; every world and scene server reads its own row back
	/// on each pulse and adopts what it finds. That is what makes a single in-game command reach
	/// processes the player is not connected to — and it means anything else that can write those
	/// rows (the Discord bot, a CMS, psql) controls the servers identically, exactly as
	/// <c>kick_requests</c> already works for accounts.
	/// </para>
	/// <para>
	/// This lives on the scene server because that is where players — and therefore admins — are.
	/// It is a partial of <see cref="SceneServerSystem"/> rather than a new behaviour so it needs
	/// no scene wiring: the scene server already loads this system.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		#region Control State

		/// <summary>
		/// Most recent control state for this scene server, published by the pulse worker and
		/// awaiting main-thread adoption.
		/// </summary>
		/// <remarks>
		/// Boxed so publication is a single atomic reference store; the struct contains a
		/// <see cref="DateTime"/>? which would not be. Same arrangement as
		/// <c>WorldServerSystem</c>.
		/// </remarks>
		private object pendingSceneControlState;

		/// <summary>
		/// Control state of each world this server hosts scenes for, published by the pulse
		/// worker. Boxed for the same reason.
		/// </summary>
		private object pendingWorldControlStates;

		/// <summary>This scene server's own control state, as last adopted. Main thread only.</summary>
		private ServerControlState sceneControlState;

		/// <summary>
		/// Control state per world server id, as last adopted. Main thread only.
		/// </summary>
		private readonly Dictionary<long, ServerControlState> worldControlStates =
			new Dictionary<long, ServerControlState>();

		/// <summary>
		/// Countdown thresholds already announced, keyed by the server being shut down.
		/// </summary>
		/// <remarks>
		/// Key 0 is this scene server; any other key is a world server id. Without this the
		/// announcement would repeat on every pulse for the whole countdown, which for a long
		/// maintenance window is a wall of identical messages rather than a warning.
		/// </remarks>
		private readonly Dictionary<long, HashSet<int>> announcedShutdownThresholds =
			new Dictionary<long, HashSet<int>>();

		/// <summary>
		/// Remaining-time marks, in seconds, at which players are warned about a shutdown.
		/// </summary>
		/// <remarks>
		/// Chosen so a short countdown still produces a warning: a 60 second shutdown announces
		/// at 60, 30 and 10. The countdown is announced when it crosses a mark rather than at
		/// fixed intervals, so the pulse rate does not decide how often players are told.
		/// </remarks>
		private static readonly int[] ShutdownAnnounceSeconds = { 900, 600, 300, 120, 60, 30, 10 };

		/// <summary>Key used for this scene server in <see cref="announcedShutdownThresholds"/>.</summary>
		private const long SelfShutdownKey = 0;

		/// <summary>
		/// Set once the shutdown disconnects have been issued; the next pass stops the process.
		/// </summary>
		/// <remarks>
		/// See the note in <see cref="ProcessControlState"/> — the delay exists so the disconnect
		/// notices reach the players before the socket goes.
		/// </remarks>
		private bool shutdownQuitPending;

		/// <summary>
		/// Worlds whose shutdown deadline this server has already acted on.
		/// </summary>
		/// <remarks>
		/// Cleared whenever a world's deadline changes or is cancelled, so a rescheduled shutdown
		/// is acted on again. See the use site in <see cref="ProcessControlState"/>.
		/// </remarks>
		private readonly HashSet<long> worldShutdownsApplied = new HashSet<long>();

		/// <summary>
		/// Publishes this scene server's control state for the main thread to adopt.
		/// Called from the pulse worker.
		/// </summary>
		/// <param name="state">Control state read back by the pulse.</param>
		private void PublishControlState(ServerControlState state)
		{
			Volatile.Write(ref pendingSceneControlState, state);
		}

		/// <summary>
		/// Whether this scene server is currently locked, as last adopted.
		/// </summary>
		/// <remarks>
		/// Read from the pulse worker as well as the main thread. Backed by
		/// <c>ISceneServerRuntimeData.IsLocked</c>, a bool, whose reads and writes are atomic —
		/// a stale value costs at most one pulse of taking work it should not have taken.
		/// </remarks>
		private bool IsLockedNow()
		{
			return Server != null &&
				Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData) &&
				runtimeData.IsLocked;
		}

		/// <summary>
		/// Reads the control state of every world this server hosts scenes for and publishes it
		/// for the main thread. Called from the pulse worker.
		/// </summary>
		/// <param name="hostedWorldServerIDs">World server ids to read.</param>
		private async Task FetchWorldControlStatesAsync(List<long> hostedWorldServerIDs)
		{
			if (hostedWorldServerIDs == null || hostedWorldServerIDs.Count == 0)
			{
				Volatile.Write(ref pendingWorldControlStates, null);
				return;
			}

			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out var worldServerService))
			{
				return;
			}

			var states = new Dictionary<long, ServerControlState>(hostedWorldServerIDs.Count);
			for (int i = 0; i < hostedWorldServerIDs.Count; ++i)
			{
				long worldServerID = hostedWorldServerIDs[i];
				DatabaseResult<ServerControlState> result = await worldServerService.FetchControlStateAsync(worldServerID);
				if (!result.IsSuccess)
				{
					/* A world whose row cannot be read is deliberately skipped rather than
					 * treated as shutting down. The conservative direction here is to keep
					 * playing: a transient database failure must not evict everyone from a world
					 * that is perfectly healthy. A world that is genuinely gone is handled by
					 * the stale-scene sweeps instead. */
					continue;
				}
				states[worldServerID] = result.Data;
			}

			Volatile.Write(ref pendingWorldControlStates, states);
		}

		/// <summary>
		/// Adopts published control state and acts on any shutdown that is due. Main thread only,
		/// driven from the periodic pulse.
		/// </summary>
		/// <returns>
		/// <c>false</c> when this server is stopping and the rest of the pulse should be skipped.
		/// Once the process is on its way out there is nothing to gain from another heartbeat or
		/// another round of scene work, and both would enqueue database calls that the teardown
		/// then has to wait on.
		/// </returns>
		private bool ProcessControlState()
		{
			object publishedScene = Volatile.Read(ref pendingSceneControlState);
			if (publishedScene != null)
			{
				Volatile.Write(ref pendingSceneControlState, null);
				AdoptSceneControlState((ServerControlState)publishedScene);
			}

			object publishedWorlds = Volatile.Read(ref pendingWorldControlStates);
			if (publishedWorlds != null)
			{
				Volatile.Write(ref pendingWorldControlStates, null);
				AdoptWorldControlStates((Dictionary<long, ServerControlState>)publishedWorlds);
			}

			DateTime nowUtc = DateTime.UtcNow;

			/* Quitting is deferred by one tick after the players have been cleared.
			 *
			 * DisconnectWithNotice relies on Disconnect(false), which only delivers its notice
			 * because the current tick's outgoing data is flushed before the socket closes.
			 * Calling Quit in the same pass races that flush, and the players who were about to
			 * be told why the server went away would simply find it gone. One tick costs nothing
			 * against a countdown measured in seconds. */
			if (shutdownQuitPending)
			{
				Log.Warning("SceneServerSystem", "Maintenance disconnects flushed; stopping the scene server.");

				/* Quit runs Server.PerformShutdown — the ordinary graceful teardown, which saves
				 * every character, hands back its session claim and deletes this server's scene
				 * rows. Fully qualified because ServerBehaviour exposes a `Server` property that
				 * shadows the type name. */
				FishMMO.Server.Implementation.Server.Quit();
				return false;
			}

			// This scene server's own shutdown clears everyone off it and stops the process.
			if (sceneControlState.HasShutdown)
			{
				AnnounceShutdown(SelfShutdownKey, null, sceneControlState, nowUtc, "This scene server");

				if (nowUtc >= sceneControlState.ShutdownAtUtc.Value)
				{
					Log.Warning("SceneServerSystem", "Scheduled shutdown deadline reached; clearing players.");
					DisconnectCharacters(null);
					shutdownQuitPending = true;
					return false;
				}
			}

			// A world shutting down clears that world's characters off this server. This process
			// keeps running: it may be hosting scenes for other worlds, and taking it down for
			// one of them would be an outage for the rest.
			foreach (var kvp in worldControlStates)
			{
				ServerControlState worldState = kvp.Value;
				if (!worldState.HasShutdown)
				{
					continue;
				}

				AnnounceShutdown(kvp.Key, kvp.Key, worldState, nowUtc, "The world");

				/* Acted on once per deadline, not once per pulse.
				 *
				 * Clearing the world's characters is idempotent — the second pass finds nobody —
				 * but the decision is not free: it walks every resident on this server and logs
				 * that the deadline was reached. A world row normally disappears seconds later
				 * (the world server deletes its registration as it stops), which would end the
				 * repetition on its own; a row left behind by a world server that is simply not
				 * running would not, and this would log and re-scan every five seconds for as
				 * long as this scene server hosts one of its scenes. */
				if (nowUtc >= worldState.ShutdownAtUtc.Value && worldShutdownsApplied.Add(kvp.Key))
				{
					Log.Warning("SceneServerSystem",
						$"World server {kvp.Key} reached its shutdown deadline; clearing its characters from this scene server.");
					DisconnectCharacters(kvp.Key);
				}
			}

			return true;
		}

		/// <summary>
		/// Adopts this scene server's control state, logging transitions.
		/// </summary>
		private void AdoptSceneControlState(ServerControlState state)
		{
			if (Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData) &&
				runtimeData.IsLocked != state.Locked)
			{
				runtimeData.IsLocked = state.Locked;
				Log.Warning("SceneServerSystem", state.Locked
					? "This scene server is now LOCKED. The world server will stop routing players here and no new scenes will be loaded."
					: "This scene server is now UNLOCKED and accepting work again.");
			}

			if (sceneControlState.ShutdownAtUtc != state.ShutdownAtUtc)
			{
				if (!state.HasShutdown)
				{
					announcedShutdownThresholds.Remove(SelfShutdownKey);
					Log.Warning("SceneServerSystem", "Scheduled shutdown cancelled.");
				}
				else
				{
					// A rescheduled shutdown is a new countdown, so previously announced marks
					// must not suppress warnings for the new deadline.
					announcedShutdownThresholds.Remove(SelfShutdownKey);
					Log.Warning("SceneServerSystem", $"Shutdown scheduled for {state.ShutdownAtUtc.Value:u}.");
				}
			}

			sceneControlState = state;
		}

		/// <summary>
		/// Adopts the control state of the worlds this server hosts scenes for.
		/// </summary>
		private void AdoptWorldControlStates(Dictionary<long, ServerControlState> states)
		{
			// Forget worlds this server no longer hosts anything for, so their countdown
			// bookkeeping does not accumulate for the life of the process.
			if (worldControlStates.Count > 0)
			{
				List<long> gone = null;
				foreach (long worldServerID in worldControlStates.Keys)
				{
					if (!states.ContainsKey(worldServerID))
					{
						(gone ??= new List<long>()).Add(worldServerID);
					}
				}
				if (gone != null)
				{
					for (int i = 0; i < gone.Count; ++i)
					{
						worldControlStates.Remove(gone[i]);
						announcedShutdownThresholds.Remove(gone[i]);
						worldShutdownsApplied.Remove(gone[i]);
					}
				}
			}

			foreach (var kvp in states)
			{
				if (worldControlStates.TryGetValue(kvp.Key, out ServerControlState previous) &&
					previous.ShutdownAtUtc != kvp.Value.ShutdownAtUtc)
				{
					// Cancelled or rescheduled: either way the old countdown is void, and a new
					// deadline has to be actionable again.
					announcedShutdownThresholds.Remove(kvp.Key);
					worldShutdownsApplied.Remove(kvp.Key);
				}
				worldControlStates[kvp.Key] = kvp.Value;
			}
		}

		/// <summary>
		/// Warns affected players once per countdown mark that has been crossed.
		/// </summary>
		/// <param name="key">Bookkeeping key: <see cref="SelfShutdownKey"/> or a world server id.</param>
		/// <param name="worldServerID">World to warn, or <c>null</c> to warn everyone on this server.</param>
		/// <param name="state">Control state carrying the deadline.</param>
		/// <param name="nowUtc">Current time.</param>
		/// <param name="subject">What is going down, for the message text.</param>
		private void AnnounceShutdown(long key, long? worldServerID, ServerControlState state, DateTime nowUtc, string subject)
		{
			double remaining = state.SecondsUntilShutdown(nowUtc);
			if (remaining <= 0.0)
			{
				return;
			}

			if (!announcedShutdownThresholds.TryGetValue(key, out HashSet<int> announced))
			{
				announcedShutdownThresholds[key] = announced = new HashSet<int>();
			}

			/* Announce the tightest mark the countdown has already passed that has not been
			 * announced yet. Walking from the largest down and stopping at the first match means
			 * a shutdown scheduled inside a mark — "shutdown 45" — still warns immediately, at
			 * 30, rather than waiting for the next mark below it. */
			for (int i = 0; i < ShutdownAnnounceSeconds.Length; ++i)
			{
				int mark = ShutdownAnnounceSeconds[i];
				if (remaining > mark || announced.Contains(mark))
				{
					continue;
				}

				announced.Add(mark);
				BroadcastSystemMessage(worldServerID,
					$"{subject} is going down for maintenance in {DescribeDuration(mark)}.");
				return;
			}
		}

		/// <summary>
		/// Renders a countdown mark the way a person would say it.
		/// </summary>
		private static string DescribeDuration(int seconds)
		{
			if (seconds < 60)
			{
				return $"{seconds} seconds";
			}
			int minutes = seconds / 60;
			return minutes == 1 ? "1 minute" : $"{minutes} minutes";
		}

		/// <summary>
		/// Clears this scene server's shutdown schedule, and the lock that came with it, as the
		/// process exits.
		/// </summary>
		/// <remarks>
		/// Without this a scheduled scene-server shutdown is a restart loop. Unlike the world
		/// server, a scene server does not delete its <c>scene_servers</c> row on the way out —
		/// only its scene rows — so the row survives with a deadline that has now passed. The
		/// AppHealthMonitor restarts the process, registration preserves the operator columns by
		/// design, the first pulse reads a deadline in the past and stops again, and round it
		/// goes until the monitor's circuit breaker gives up.
		/// <para>
		/// The lock goes with it. A shutdown locks the server in order to drain it for that
		/// shutdown; once it has happened the lock has served its purpose, and leaving it set
		/// would strand a single-scene-server deployment outright — the world server would refuse
		/// to route anyone there, so nobody could get in to run <c>/admin unlockscene</c>. This
		/// leaves a restarted scene server in the same state the world server comes back in.
		/// </para>
		/// <para>
		/// Blocking is deliberate and bounded, matching every other database call on this
		/// teardown path: the write has to land before the process goes.
		/// </para>
		/// </remarks>
		private void ClearConsumedShutdownOnTeardown()
		{
			if (!sceneControlState.HasShutdown)
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService) ||
				!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData) ||
				runtimeData.ID <= 0)
			{
				return;
			}

			long sceneServerID = runtimeData.ID;

			try
			{
				if (!UnitySyncOverAsync.TryRun(
					cancellationToken => sceneServerService.SetShutdownAsync(sceneServerID, null, cancellationToken),
					out DatabaseResult clearResult,
					ShutdownClearTimeoutMs) ||
					!clearResult.IsSuccess)
				{
					Log.Warning("SceneServerSystem",
						$"Could not clear the consumed shutdown schedule for scene server {sceneServerID}. " +
						"If this process is restarted automatically it will stop again immediately; clear " +
						"scene_servers.shutdown_at_utc by hand.");
					return;
				}

				if (!UnitySyncOverAsync.TryRun(
					cancellationToken => sceneServerService.SetLockedAsync(sceneServerID, false, cancellationToken),
					out DatabaseResult unlockResult,
					ShutdownClearTimeoutMs) ||
					!unlockResult.IsSuccess)
				{
					Log.Warning("SceneServerSystem",
						$"Cleared the shutdown schedule for scene server {sceneServerID} but could not unlock it. " +
						"A restarted process will come back locked; use /admin unlockscene or clear scene_servers.locked.");
				}
			}
			catch (Exception ex)
			{
				Log.Error("SceneServerSystem", $"Failed to clear the consumed shutdown schedule: {ex}");
			}
		}

		/// <summary>Bound on each teardown write that clears the consumed shutdown.</summary>
		private const int ShutdownClearTimeoutMs = 5_000;

		#endregion

		#region Player Notification

		/// <summary>
		/// Sends a system-channel message to characters on this scene server.
		/// </summary>
		/// <param name="worldServerID">Restrict to one world, or <c>null</c> for everyone here.</param>
		/// <param name="text">Message body.</param>
		private void BroadcastSystemMessage(long? worldServerID, string text)
		{
			if (Server?.NetworkWrapper == null ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				return;
			}

			foreach (var kvp in charMapping.ConnectionCharacters)
			{
				IPlayerCharacter character = kvp.Value;
				if (character == null ||
					(worldServerID.HasValue && character.WorldServerID != worldServerID.Value))
				{
					continue;
				}

				NetworkConnection conn = kvp.Key;
				if (conn == null || !conn.IsActive)
				{
					continue;
				}

				Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
				{
					Channel = ChatChannel.System,
					Text = text,
				}, true, FishNet.Transporting.Channel.Reliable);
			}
		}

		/// <summary>
		/// Disconnects characters with a maintenance notice.
		/// </summary>
		/// <remarks>
		/// Snapshotted before disconnecting: the disconnect path removes entries from the very
		/// map being enumerated, and relying on FishNet deferring that is relying on its
		/// scheduling rather than on anything this loop controls.
		/// <para>
		/// Terminal, so the client stops its reconnect loop and shows the reason rather than
		/// spending ten attempts dialling a server that is going away.
		/// </para>
		/// </remarks>
		/// <param name="worldServerID">Restrict to one world, or <c>null</c> for everyone here.</param>
		private void DisconnectCharacters(long? worldServerID)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				return;
			}

			List<NetworkConnection> leaving = null;
			foreach (var kvp in charMapping.ConnectionCharacters)
			{
				IPlayerCharacter character = kvp.Value;
				if (character == null ||
					(worldServerID.HasValue && character.WorldServerID != worldServerID.Value))
				{
					continue;
				}
				(leaving ??= new List<NetworkConnection>()).Add(kvp.Key);
			}

			if (leaving == null)
			{
				return;
			}

			/* Not a player quitting mid-fight. Without this a character in combat would be held
			 * as a combat-logout body on a server that is about to stop, keeping its session
			 * claim until the linger expired — on a process that will not be there to release
			 * it. See ICharacterSystem.SuppressCombatLingerOnDisconnect. */
			Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem);

			for (int i = 0; i < leaving.Count; ++i)
			{
				NetworkConnection conn = leaving[i];
				if (conn == null || !conn.IsActive)
				{
					continue;
				}

				characterSystem?.SuppressCombatLingerOnDisconnect(conn);
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerMaintenance, terminal: true);
			}

			Log.Warning("SceneServerSystem",
				$"Disconnected {leaving.Count} character(s) for maintenance" +
				(worldServerID.HasValue ? $" (world {worldServerID.Value})." : "."));
		}

		#endregion
	}
}
