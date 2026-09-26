using FishNet.Connection;
using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Handles world server registration and heartbeat (pulse) updates in the database.
	/// Periodically updates the world server's status and character count.
	/// Database operations are async to avoid blocking the main thread.
	/// </summary>
	[CreateAssetMenu(fileName = "WorldServerSystem", menuName = "FishMMO/Server/WorldServer/World Server System", order = 1)]
	[RequiresDataContainer(typeof(WorldServerSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class WorldServerSystem : ServerBehaviour, IWorldServerSystem
	{
		/// <summary>
		/// Interval (in seconds) between heartbeat pulses to the database.
		/// </summary>
		[SerializeField] private float pulseRate = 5.0f;

		/// <summary>
		/// Maximum time a shutdown database call may block the main thread. Shorter than startup:
		/// process exit must not wait on an unresponsive database.
		/// </summary>
		private const int dbShutdownTimeoutMs = 5_000;

		/// <summary>Writes this process's bandwidth to the database once a minute. Null until initialised.</summary>
		private ServerBandwidthRecorder bandwidthRecorder;

		/// <summary>
		/// Interval (in seconds) between heartbeat pulses to the database.
		/// </summary>
		public float PulseRate => pulseRate;

		/// <summary>
		/// Initializes the world server system, validates dependencies, registers
		/// this world server in the database, and starts periodic pulse callbacks.
		/// </summary>
		/// <returns>The initialization status.</returns>
		/// <summary>
		/// Synchronous entry point. This system registers itself in the database, so it must be
		/// initialized through <see cref="InitializeOnceAsync"/>; reaching this method means the
		/// asynchronous startup chain was bypassed.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			Log.Error("WorldServerSystem",
				"InitializeOnce called directly. This system performs database I/O and must be " +
				"initialized via InitializeOnceAsync (Server drives this through the async startup chain).");
			return ServerComponentInitializationStatus.InitializationFailed;
		}

		/// <summary>
		/// Initializes the system and registers it in the database without blocking the Unity
		/// main thread.
		/// </summary>
		/// <remarks>
		/// Awaits here deliberately capture Unity's SynchronizationContext (no
		/// <c>ConfigureAwait(false)</c>), so execution resumes on the main thread and the Unity
		/// and FishNet APIs used below stay legal.
		/// </remarks>
		public override async Task<ServerComponentInitializationStatus> InitializeOnceAsync(CancellationToken cancellationToken)
		{
			if (Server == null)
			{
				_ = Log.Error("WorldServerSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				_ = Log.Error("WorldServerSystem", "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out _))
			{
				_ = Log.Error("WorldServerSystem", "InitializeOnce: IWorldServerService not found");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Configuration.TryGetString("ServerName", out _))
			{
				_ = Log.Error("WorldServerSystem", "InitializeOnce: ServerName not configured");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			/* Registration is mandatory, not best-effort.
			 *
			 * These two lookups used to gate the registration call inside a single combined
			 * `if`, so a failure of either skipped registration and still returned Initialized.
			 * A world server in that state comes up healthy and accepts logins with its database
			 * ID left at 0 — and every routing decision is keyed on that ID. FetchAvailableAsync
			 * matches no scene rows, so no player is ever assigned an instance; they sit in the
			 * open-world queue until the 45s TTL sweep kicks them, with nothing in the log
			 * saying why. Fail startup instead, matching SceneServerSystem, which already
			 * returns FailedToFindRequiredDependency for the same address lookup. */
			if (!Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server))
			{
				_ = Log.Error("WorldServerSystem", "InitializeOnce: Could not get server IP address");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.BehaviourRegistry.TryGet(out IWorldSceneSystem _))
			{
				_ = Log.Error("WorldServerSystem", "InitializeOnce: IWorldSceneSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			int characterCount = Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var sceneData) ? sceneData.ConnectionCount : 0;

			// This asset outlives a play session in the editor; no countdown carries over.
			shutdownCountdown.Clear();
			shutdownQuitIssued = false;
			System.Threading.Volatile.Write(ref pendingControlState, null);

			if (!await RegisterAsync(server.Address, server.Port, characterCount, cancellationToken))
			{
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(PulseRate, OnPeriodicPulse);
			}

			// Bandwidth statistics for the Control Panel. See ServerBandwidthRecorder.
			bandwidthRecorder = ServerBandwidthRecorder.TryStart(Server, ServerType.World);

			_ = Log.Debug("WorldServerSystem", $"Initialized (PulseRate={PulseRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Deregisters the world server from the database and unregisters periodic callbacks.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("WorldServerSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPulse);
			}

			// Deregister world server from database on shutdown
			if (Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var runtimeData) &&
				runtimeData.ID > 0 &&
				Server.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out var worldServerService))
			{
				try
				{
					// BLOCKING THE MAIN THREAD DURING SHUTDOWN IS INTENTIONAL: UnitySyncOverAsync keeps
					// the work off Unity's SynchronizationContext and bounds the wait. At this point
					// the server is shutting down, so blocking momentarily is acceptable and ensures
					// the DB cleanup completes before process exit.
					if (UnitySyncOverAsync.TryRun(
						cancellationToken => worldServerService.DeleteAsync(runtimeData.ID, cancellationToken),
						out DatabaseResult deleteResult,
						dbShutdownTimeoutMs))
					{
						if (!deleteResult.IsSuccess)
						{
							Log.Warning("WorldServerSystem", $"Failed to deregister world server from DB (ServerID={runtimeData.ID}): [{deleteResult.ErrorCode}] {deleteResult.ErrorMessage}");
						}
					}
					else
					{
						Log.Warning("WorldServerSystem", $"World server deregistration timed out after {dbShutdownTimeoutMs}ms (ServerID={runtimeData.ID})");
					}
				}
				catch (Exception ex)
				{
					Log.Error("WorldServerSystem", $"Failed to deregister world server from DB (ServerID={runtimeData.ID}): {ex}");
				}
			}

			// Last, so deregistration has the shutdown budget first: a bounded final bandwidth sample.
			bandwidthRecorder?.Stop();
			bandwidthRecorder = null;
		}

		/// <summary>
		/// Registers the world server in the database without blocking the Unity main thread.
		/// </summary>
		/// <param name="serverAddress">Address string.</param>
		/// <param name="port">Port number.</param>
		/// <param name="characterCount">Character count to register.</param>
		/// <param name="cancellationToken">Cancelled when the server shuts down mid-startup.</param>
		/// <returns><c>true</c> when the server was registered.</returns>
		public async Task<bool> RegisterAsync(string serverAddress, ushort port, int characterCount, CancellationToken cancellationToken = default)
		{
			if (!Server.DataContainerRegistry.TryGet(out IWorldServerSystemRuntimeData data))
			{
				_ = Log.Error("WorldServerSystem", "Failed to get IWorldServerSystemRuntimeData.");
				return false;
			}
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out var worldServerService))
			{
				_ = Log.Error("WorldServerSystem", "Failed to resolve IWorldServerService from database service registry.");
				return false;
			}

			if (!Server.Configuration.TryGetString("ServerName", out string name))
			{
				_ = Log.Error("WorldServerSystem", "ServerName not configured.");
				return false;
			}

			DatabaseResult<(long ServerId, WorldServerData ServerData, ServerControlState Control)> result =
				await worldServerService.PersistAsync(name, serverAddress, port, characterCount, data.IsLocked, cancellationToken);
			// Stamped as the reply arrives: it anchors the database-measured shutdown countdown.
			double registrationReadAt = MonotonicClock.NowSeconds;

			if (!result.IsSuccess)
			{
				_ = Log.Error("WorldServerSystem", $"Failed to register world server: [{result.ErrorCode}] {result.ErrorMessage}");
				return false;
			}

			data.ID = result.Data.ServerId;

			/* Adopt the operator state the row came back with — lock AND scheduled shutdown — through
			 * the same method every heartbeat uses.
			 *
			 * PersistAsync deliberately keeps an existing row's operator state on conflict, so a
			 * world an operator locked, or scheduled for shutdown, and that then restarted without
			 * deregistering (a crash, or a deregistration that timed out) still carries it in the
			 * database. Dropping it here left the server unaware until the first pulse's state was
			 * adopted, two pulses in: WorldServerAuthenticator admitted players to the locked world
			 * for that window, and a scheduled shutdown went unannounced. The registration reply
			 * now carries both (issue #267). */
			ApplyControlState(new ServerControlReading(result.Data.Control, registrationReadAt));
			return true;
		}

		/// <summary>
		/// Sends a heartbeat/pulse update with the current character count.
		/// </summary>
		/// <param name="characterCount">Current character count.</param>
		/// <remarks>
		/// One pulse at a time. Each pulse reads back the row's lock and shutdown state and
		/// publishes it for the main thread; while the database was stalled, pulses overlapped,
		/// and two finishing out of order could publish the older state over the newer — a lock
		/// lifted and then re-applied, or a cancelled shutdown brought back. With one in flight,
		/// publications are in the order the reads were made. A pulse skipped here is covered by
		/// the one still running.
		/// </remarks>
		public void Pulse(int characterCount)
		{
			if (!Server.DataContainerRegistry.TryGet(out IWorldServerSystemRuntimeData data))
			{
				return;
			}

			if (!data.TryBeginPulse())
			{
				return;
			}

			// Queue async DB pulse
			if (!TryEnqueueAsyncWork(() => PulseAsync(data, characterCount)))
			{
				data.EndPulse();
				Log.Warning("WorldServerSystem", "Failed to enqueue world server pulse work item.");
			}
		}

		/// <summary>
		/// Asynchronously sends a heartbeat pulse to the database, releasing the pulse gate when done.
		/// </summary>
		/// <param name="data">This world server's runtime data; holds its ID and the pulse gate.</param>
		/// <param name="characterCount">Current number of connected characters.</param>
		private async Task PulseAsync(IWorldServerSystemRuntimeData data, int characterCount)
		{
			long serverId = data.ID;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out var worldServerService))
				{
					return;
				}
				DatabaseResult<ServerControlState> result = await worldServerService.PulseAsync(serverId, characterCount);
				// Stamped here, as the reply arrives, not when the main thread adopts it a pulse
				// later: it anchors the database-measured shutdown countdown. See ShutdownCountdown.
				ServerControlReading reading = result.IsSuccess ? ServerControlReading.ArrivedNow(result.Data) : null;
				if (!result.IsSuccess)
				{
					await Log.Warning("WorldServerSystem", $"PulseAsync DB error (ServerID={serverId}): {result.ErrorCode} - {result.ErrorMessage}");
					return;
				}

				/* Publish for the main thread to adopt.
				 *
				 * Applying it here would mean touching Unity APIs from an async worker —
				 * Server.Quit() is one — and writing a DateTime? that the main thread reads,
				 * which is not a single atomic store. Publishing the reading, a reference, through
				 * a volatile store makes publication atomic; OnPeriodicPulse picks it up on the
				 * next tick. A lock change therefore takes effect within two pulses. A shutdown's
				 * deadline does not wait on that: the reading carries its own arrival time, and
				 * the countdown runs on the monotonic clock from there. */
				System.Threading.Volatile.Write(ref pendingControlState, reading);
			}
			catch (Exception ex)
			{
				await Log.Error("WorldServerSystem", $"Error during pulse (ServerID={serverId}): {ex}");
			}
			finally
			{
				// After the publication above, so the next pulse cannot start — and publish —
				// before this one's result is in place.
				data.EndPulse();
			}
		}

		/// <summary>
		/// Most recent control state read back by a pulse, awaiting main-thread adoption.
		/// </summary>
		/// <remarks>
		/// A reference so publication is a single store, which is atomic; the struct it carries
		/// contains a <see cref="DateTime"/>? that would not be. Written by the async pulse
		/// worker, taken by <see cref="OnPeriodicPulse"/> on the main thread.
		/// </remarks>
		private ServerControlReading pendingControlState;

		/// <summary>
		/// This world server's countdown to a scheduled shutdown, on <see cref="MonotonicClock"/>.
		/// Main thread only.
		/// </summary>
		/// <remarks>
		/// Anchored from the seconds left that each read of the row measured by the database
		/// clock. It used to compare the row's instant with <c>DateTime.UtcNow</c>, so a world host
		/// running fast stopped early, a clock stepped forward stopped the world at once, and the
		/// scene servers clearing this world's players counted down to a different moment from the
		/// world itself.
		/// </remarks>
		private readonly ShutdownCountdown shutdownCountdown = new ShutdownCountdown();

		/// <summary>Set once the shutdown deadline has been acted on, so it is acted on once.</summary>
		private bool shutdownQuitIssued;

		/// <summary>
		/// Adopts the lock and shutdown state read back from this server's database row.
		/// </summary>
		/// <remarks>
		/// Main thread only. Logs each transition rather than the steady state, so the operator
		/// log shows when a lock or a shutdown took effect without a line every pulse.
		/// </remarks>
		/// <param name="reading">Control state as the database holds it, and when the read arrived.</param>
		private void ApplyControlState(ServerControlReading reading)
		{
			if (reading == null || !Server.DataContainerRegistry.TryGet(out IWorldServerSystemRuntimeData data))
			{
				return;
			}

			ServerControlState state = reading.State;

			if (data.IsLocked != state.Locked)
			{
				data.IsLocked = state.Locked;
				Log.Warning("WorldServerSystem", state.Locked
					? "This world server is now LOCKED. New logins are refused; accounts above Player are still admitted."
					: "This world server is now UNLOCKED and accepting logins again.");
			}

			// The authenticator only asks whether a shutdown is scheduled; the countdown is below.
			data.ShutdownAtUtc = state.ShutdownAtUtc;

			if (shutdownCountdown.Adopt(reading))
			{
				Log.Warning("WorldServerSystem", shutdownCountdown.IsScheduled
					? $"Shutdown scheduled for {state.ShutdownAtUtc.Value:u}, in {shutdownCountdown.SecondsRemaining(MonotonicClock.NowSeconds):F0}s by the database clock."
					: "Scheduled shutdown cancelled.");
			}

			QuitIfShutdownDue();
		}

		/// <summary>
		/// Stops the world server once its scheduled shutdown has fallen due on the monotonic
		/// countdown. Main thread only.
		/// </summary>
		/// <remarks>
		/// Checked on every periodic pulse, not only when a reading arrives: the countdown runs on
		/// this process's clock between readings, so a database that is slow to answer does not
		/// hold the shutdown back.
		/// </remarks>
		private void QuitIfShutdownDue()
		{
			if (shutdownQuitIssued || !shutdownCountdown.IsDue(MonotonicClock.NowSeconds))
			{
				return;
			}
			shutdownQuitIssued = true;

			/* The deadline has arrived. Quitting runs Server.PerformShutdown, which is the
			 * ordinary graceful teardown — it saves, releases session claims and removes this
			 * world's scene rows. Nothing here disconnects clients first: the world server holds
			 * only clients in transit between login and a scene server, and they recover through
			 * their own reconnect loop. */
			Log.Warning("WorldServerSystem", "Scheduled shutdown deadline reached; stopping the world server.");

			// Fully qualified: ServerBehaviour exposes a `Server` property that shadows the type
			// name, and Quit is a static on the type.
			FishMMO.Server.Implementation.Server.Quit();
		}

		/// <summary>
		/// Periodic callback that sends a heartbeat pulse to the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicPulse(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			// Adopt whatever the last pulse read back before issuing the next one.
			ServerControlReading published = System.Threading.Volatile.Read(ref pendingControlState);
			if (published != null)
			{
				System.Threading.Volatile.Write(ref pendingControlState, null);
				ApplyControlState(published);
			}
			else
			{
				QuitIfShutdownDue();
			}

			if (Server.BehaviourRegistry.TryGet(out IWorldSceneSystem _))
			{
				// Send a heartbeat pulse to the database with the current character count using the interface method.
				int characterCount = Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var sceneData) ? sceneData.ConnectionCount : 0;
				Pulse(characterCount);
			}
		}
	}
}