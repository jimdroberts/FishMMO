using Microsoft.Extensions.Configuration;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Orchestrates the lifecycle of application health monitors.
	/// Owns daemon-wide state (cancellation, start signal, active monitors)
	/// and pre-validates configurations at construction time.
	/// </summary>
	public sealed class DaemonOrchestrator : IAsyncDisposable, ISupervisionHost
	{
		/// <summary>
		/// Semaphore used to signal the start of a new monitoring cycle. Released by <see cref="TrySignalStart"/>.
		/// </summary>
		private readonly SemaphoreSlim startMonitoringSignal = new SemaphoreSlim(0, 1);

		/// <summary>
		/// Lock protecting concurrent access to the <see cref="activeMonitors"/> list.
		/// </summary>
		private readonly object activeMonitorsLock = new();

		/// <summary>
		/// The currently active health monitors for the current monitoring cycle.
		/// </summary>
		private readonly List<HealthMonitor> activeMonitors = [];

		/// <summary>
		/// Whether all monitored applications should be launched in headless mode.
		/// </summary>
		private readonly bool headless;

		/// <summary>
		/// The validated application configurations and their associated health checkers.
		/// </summary>
		private readonly IReadOnlyList<(AppConfig Config, IReadOnlyList<IHealthChecker> HealthCheckers)> validatedApps;

		/// <summary>
		/// The daemon's configuration, used to construct the optional control plane. Null when the
		/// daemon was constructed without one, which simply means no control plane.
		/// </summary>
		private readonly IConfiguration? configuration;

		/// <summary>
		/// The database-backed control plane, or null when this deployment has no database.
		/// </summary>
		private DaemonControlPlane? controlPlane;

		/// <summary>
		/// The read service behind pulse health checks, or null when there is no database.
		/// </summary>
		/// <remarks>
		/// Resolved once, at construction, because the health checkers are built when the
		/// monitors are — before the control plane starts — and a checker needs it in hand. It
		/// is independent of the control plane: a deployment can want pulse checks without the
		/// command queue.
		/// </remarks>
		private readonly FishMMO.Database.Npgsql.Services.Interfaces.IServerBoardService? boardService;

		/// <summary>
		/// The running control plane loop, or null when there is no control plane.
		/// </summary>
		private Task? controlPlaneTask;

		/// <summary>
		/// Maximum time to wait for the control plane loop to finish during shutdown.
		/// </summary>
		private static readonly TimeSpan controlPlaneStopTimeout = TimeSpan.FromSeconds(15);

		/// <summary>
		/// Lock protecting <see cref="cycleTasks"/>, <see cref="cycleAcceptsRevivals"/> and
		/// <see cref="cycleToken"/>.
		/// </summary>
		/// <remarks>
		/// Deliberately separate from <see cref="activeMonitorsLock"/>. Both are taken during a
		/// revival and during cycle teardown, and a single lock covering both would have to be held
		/// across the wait loop's decision to conclude — which is exactly the window a revival races.
		/// They are never taken nested, so there is no ordering to get wrong.
		/// </remarks>
		private readonly object cycleLock = new();

		/// <summary>
		/// The tasks the current monitoring cycle is waiting on: one per live monitor, plus a
		/// placeholder for each outstanding revival reservation. Null when no cycle is running.
		/// </summary>
		/// <remarks>
		/// Mutable, and that is the point. When an operator revives a monitor whose loop has ended,
		/// its new monitoring task is added here so the cycle waits on it like any other. The cycle
		/// concludes only when this list drains — which is what keeps a headless daemon from
		/// shutting down on top of a freshly revived application.
		/// </remarks>
		private List<Task>? cycleTasks;

		/// <summary>
		/// Whether the current cycle can still take a monitoring task back. Cleared, under
		/// <see cref="cycleLock"/>, in the same step that observes <see cref="cycleTasks"/> empty,
		/// so a revival either gets in before the cycle concludes or is refused outright.
		/// </summary>
		private bool cycleAcceptsRevivals;

		/// <summary>
		/// The current cycle's linked cancellation token (cycle stop or daemon shutdown). Default,
		/// which is never cancelled, when no cycle is running — <see cref="cycleTasks"/> being null
		/// is what refuses revivals in that case.
		/// </summary>
		private CancellationToken cycleToken;

		/// <summary>
		/// Cancellation token source for daemon-wide shutdown.
		/// </summary>
		private readonly CancellationTokenSource daemonCts = new();
		/// <summary>
		/// Signals when the current monitoring cycle has fully completed cleanup.
		/// </summary>
		private TaskCompletionSource? cycleCompletionSource;

		/// <summary>
		/// Cancellation token source for the current monitoring cycle. Null when no cycle is active.
		/// </summary>
		private CancellationTokenSource? currentMonitoringCts;

		/// <summary>
		/// Guard flag to prevent double disposal. Set atomically via <see cref="Interlocked.CompareExchange"/>.
		/// </summary>
		private int isDisposed;

		/// <summary>
		/// Maximum time to wait for the active monitoring cycle to complete during disposal.
		/// Prevents the daemon from hanging indefinitely if the monitoring cycle is stuck.
		/// </summary>
		private static readonly TimeSpan disposeTimeout = TimeSpan.FromSeconds(30);

		/// <summary>
		/// Maximum time to wait for a force-kill request to observe cycle cleanup completion.
		/// Prevents command handlers from blocking indefinitely on stuck monitoring cycles.
		/// </summary>
		private static readonly TimeSpan forceKillWaitTimeout = TimeSpan.FromSeconds(30);

		/// <summary>
		/// Gets whether the daemon has been signalled to shut down.
		/// </summary>
		public bool IsDaemonShutdownRequested => daemonCts.IsCancellationRequested;

		/// <summary>
		/// Gets whether all monitored applications should be launched in headless mode.
		/// Used by <see cref="CommandHandler"/> to suppress the interactive console prompt.
		/// </summary>
		public bool Headless => headless;

		/// <summary>
		/// Gets the cancellation token that is signalled when the daemon is shutting down.
		/// Used by <see cref="CommandHandler"/> for cancellable I/O operations.
		/// </summary>
		public CancellationToken DaemonShutdownToken => daemonCts.Token;

		/// <summary>
		/// Gets whether the daemon shut down automatically after a headless monitoring cycle completed.
		/// When true, all monitors exhausted their restart attempts or failed initial launch,
		/// and the daemon should exit with a non-zero exit code to prevent systemd restart loops.
		/// Backed by an int field for thread-safe reads via <see cref="Volatile"/>.
		/// </summary>
		private int headlessCycleCompleted;

		/// <inheritdoc cref="headlessCycleCompleted"/>
		public bool HeadlessCycleCompleted => Volatile.Read(ref headlessCycleCompleted) != 0;

		/// <summary>
		/// Initializes a new instance of the <see cref="DaemonOrchestrator"/> class.
		/// Applies defaults, validates, detects duplicate names, creates health checkers,
		/// and logs configuration for each app in a single pass.
		/// </summary>
		/// <param name="appConfigs">The raw application configurations from settings.</param>
		/// <param name="headless">Whether all monitored applications should be launched in headless mode.</param>
		/// <param name="configuration">
		/// The daemon's configuration, used only to construct the optional database control plane.
		/// Null, or a configuration with no database credentials, means the daemon supervises exactly
		/// as it always has with no control plane at all.
		/// </param>
		/// <exception cref="InvalidOperationException">Thrown when no application configurations are provided, a configuration entry is invalid, or duplicate application names are detected.</exception>
		public DaemonOrchestrator(IReadOnlyList<AppConfig> appConfigs, bool headless, IConfiguration? configuration = null)
		{
			ArgumentNullException.ThrowIfNull(appConfigs);

			if (appConfigs.Count == 0)
			{
				throw new InvalidOperationException("No application configurations were provided.");
			}

			var apps = new List<(AppConfig, IReadOnlyList<IHealthChecker>)>(appConfigs.Count);
			var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var appConfig in appConfigs)
			{
				if (!appConfig.TryApplyDefaultsAndValidate(out string error))
				{
					throw new InvalidOperationException($"Invalid configuration for application '{appConfig.Name}': {error}");
				}

				if (!seenNames.Add(appConfig.Name))
				{
					throw new InvalidOperationException($"Duplicate application name '{appConfig.Name}'. Each application must have a unique Name.");
				}

				var healthCheckers = HealthCheckerFactory.Create(appConfig.PortTypes, appConfig, boardService);
				apps.Add((appConfig, healthCheckers));

				LogAppConfiguration(appConfig);
			}

			validatedApps = apps;

			this.headless = headless;
			this.configuration = configuration;
			boardService = DaemonControlPlane.TryCreateBoardService(configuration);

			Log.Info("Orchestration", $"Loaded {apps.Count} valid application configuration(s). Headless: {headless}");
		}

		/// <summary>
		/// Logs the full configuration for a single application. Called once at startup.
		/// </summary>
		/// <param name="appConfig">The application configuration to log.</param>
		private static void LogAppConfiguration(AppConfig appConfig)
		{
			var appDetails = new Dictionary<string, object>
			{
				{ "ApplicationExePath", appConfig.ApplicationExePath },
				{ "MonitoredPort", appConfig.MonitoredPort },
				{ "PortTypes", appConfig.PortTypes.Count > 0 ? string.Join(", ", appConfig.PortTypes) : "(process-only)" },
				{ "LaunchArguments", appConfig.LaunchArguments },
				{ "CheckInterval", $"{appConfig.CheckIntervalSeconds}s" },
				{ "LaunchDelay", $"{appConfig.LaunchDelaySeconds}s" },
				{ "CpuThreshold", $"{appConfig.CpuThresholdPercent}%" },
				{ "MemoryThreshold", $"{appConfig.MemoryThresholdMB}MB" },
				{ "GracefulShutdownTimeout", $"{appConfig.GracefulShutdownTimeoutSeconds}s" },
				{ "ForceKillTimeout", $"{appConfig.ForceKillTimeoutSeconds}s" },
				{ "HealthCheckHost", appConfig.HealthCheckHost },
				{ "ResourceCheckFailureThreshold", appConfig.ResourceCheckFailureThreshold },
				{ "InitialHealthCheckDelay", $"{appConfig.InitialHealthCheckDelaySeconds}s" },
				{ "PostLaunchSettleDelay", $"{appConfig.PostLaunchSettleDelaySeconds}s" },
				{ "InitialRestartDelay", $"{appConfig.InitialRestartDelaySeconds}s" },
				{ "MaxRestartDelay", $"{appConfig.MaxRestartDelaySeconds}s" },
				{ "MaxRestartAttempts", appConfig.MaxRestartAttempts },
				{ "PortCheckTimeout", $"{appConfig.PortCheckTimeoutMs}ms" },
				{ "CircuitBreakerFailureThreshold", appConfig.CircuitBreakerFailureThreshold }
			};

			Log.Info("Orchestration", $"Application Configuration for {appConfig.Name}:", data: appDetails);
		}

		/// <summary>
		/// Checks whether monitoring is currently active and not cancelled.
		/// Uses <see cref="Volatile.Read"/> for lock-free thread safety on the CTS reference.
		/// </summary>
		/// <returns>True if monitoring is active and not cancelled; otherwise, false.</returns>
		public bool IsMonitoringActive()
		{
			var cts = Volatile.Read(ref currentMonitoringCts);
			return cts != null && !cts.IsCancellationRequested;
		}

		/// <summary>
		/// Attempts to release the start signal semaphore to trigger a new monitoring cycle.
		/// Safely handles the case where the semaphore is already at its maximum count.
		/// </summary>
		/// <returns>True if the signal was released; false if it was already signalled.</returns>
		public bool TrySignalStart()
		{
			try
			{
				startMonitoringSignal.Release();
				return true;
			}
			catch (SemaphoreFullException)
			{
				Log.Warning("DaemonCommand", "Start signal already pending.");
				return false;
			}
			catch (ObjectDisposedException)
			{
				// Semaphore was disposed during shutdown — safe to ignore.
				Log.Warning("DaemonCommand", "Cannot signal start: daemon is shutting down.");
				return false;
			}
		}

		/// <summary>
		/// Thread-safe cancellation of the current monitoring cycle.
		/// Uses <see cref="Volatile.Read"/> for lock-free access.
		/// Safely handles the case where the CTS was disposed by the monitoring cycle's finally block.
		/// </summary>
		public void CancelCurrentMonitoring()
		{
			try
			{
				Volatile.Read(ref currentMonitoringCts)?.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// CTS was disposed by the monitoring cycle's finally block — safe to ignore.
			}
		}

		/// <summary>
		/// Takes a thread-safe snapshot of the current active monitors list.
		/// </summary>
		/// <returns>A new list containing all active monitors at the time of the snapshot.</returns>
		private List<HealthMonitor> TakeMonitorSnapshot()
		{
			lock (activeMonitorsLock)
			{
				return new List<HealthMonitor>(activeMonitors);
			}
		}

		/// <summary>
		/// Cancels monitoring and force-kills all active monitored processes.
		/// Captures the cycle completion source before cancellation to prevent
		/// the race where the cycle's finally block nulls it before callers can await it.
		/// Returns only after all processes have been terminated.
		/// Thread-safe: takes a snapshot of active monitors under lock before killing.
		/// </summary>
		/// <returns>The captured cycle completion task (if a cycle was active), or null.</returns>
		public async Task<Task?> ForceKillAllAsync()
		{
			// Capture the TCS BEFORE cancelling so we have a stable reference.
			var tcs = Volatile.Read(ref cycleCompletionSource);
			var capturedCycleCompletion = tcs?.Task;

			CancelCurrentMonitoring();

			var snapshot = TakeMonitorSnapshot();

			if (snapshot.Count > 0)
			{
				var tasks = new Task[snapshot.Count];
				for (int i = 0; i < snapshot.Count; i++)
				{
					tasks[i] = snapshot[i].KillApplicationAsync();
				}

				try
				{
					await Task.WhenAll(tasks).WaitAsync(forceKillWaitTimeout, daemonCts.Token);
				}
				catch (TimeoutException)
				{
					Log.Warning("Orchestration", $"Force-kill monitor termination did not complete within {forceKillWaitTimeout.TotalSeconds}s.");
				}
				catch (OperationCanceledException)
				{
					// Daemon is shutting down; caller will continue cleanup through cycle completion.
				}
			}

			return capturedCycleCompletion;
		}

		/// <summary>
		/// Waits for monitoring cycle cleanup to finish with a bounded timeout.
		/// </summary>
		/// <param name="cycleCompletion">The cycle completion task captured from <see cref="ForceKillAllAsync"/>.</param>
		/// <returns>A task representing the wait operation.</returns>
		public async Task WaitForCycleCompletionAsync(Task? cycleCompletion)
		{
			if (cycleCompletion == null)
			{
				return;
			}

			try
			{
				await cycleCompletion.WaitAsync(forceKillWaitTimeout, daemonCts.Token);
			}
			catch (TimeoutException)
			{
				Log.Warning("Orchestration", $"Force-kill cleanup did not complete within {forceKillWaitTimeout.TotalSeconds}s.");
			}
			catch (OperationCanceledException)
			{
				// Daemon is shutting down; no further waiting is required.
			}
		}

		/// <summary>
		/// Gets the validated application configurations this daemon supervises, in configured order.
		/// </summary>
		/// <remarks>
		/// This list is loaded from this host's own <c>appsettings.json</c> and is the only place a
		/// name may resolve to something launchable. <see cref="DaemonControlPlane"/> resolves every
		/// command's application name against it, which is what keeps a database row from naming
		/// anything this daemon was not already configured to run.
		/// </remarks>
		public IReadOnlyList<AppConfig> ConfiguredApplications
		{
			get
			{
				var configs = new List<AppConfig>(validatedApps.Count);
				foreach (var (config, _) in validatedApps)
				{
					configs.Add(config);
				}
				return configs;
			}
		}

		/// <summary>
		/// Finds the live monitor for a configured application name.
		/// </summary>
		/// <param name="name">The application name, matched case-insensitively as configuration names are.</param>
		/// <param name="monitor">The live monitor when one exists for the current cycle; otherwise, null.</param>
		/// <returns>True when a live monitor was found; otherwise, false.</returns>
		public bool TryGetActiveMonitor(string name, out HealthMonitor? monitor)
		{
			monitor = null;
			if (string.IsNullOrWhiteSpace(name))
			{
				return false;
			}

			lock (activeMonitorsLock)
			{
				foreach (var candidate in activeMonitors)
				{
					if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
					{
						monitor = candidate;
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Reserves a place in the running monitoring cycle for a monitor about to be revived.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The reservation is a placeholder task added to <see cref="cycleTasks"/>. It is incomplete
		/// until disposed, so the cycle's wait loop cannot observe the list empty and conclude while
		/// a revival is being launched. That is the whole mechanism: the caller may safely start a
		/// process knowing the cycle will still be there to take its monitoring task.
		/// </para>
		/// <para>
		/// Refused when there is no cycle, when the cycle has already concluded, or when the cycle or
		/// the daemon is cancelled. A refusal is what stops an unwatched process from being launched.
		/// </para>
		/// </remarks>
		/// <param name="monitorName">The monitor's name, for logging only.</param>
		/// <param name="refusal">A sentence saying why, when the reservation was refused.</param>
		/// <returns>A reservation, or null when supervision cannot be resumed.</returns>
		public ISupervisionReservation? TryReserveRevival(string monitorName, out string refusal)
		{
			lock (cycleLock)
			{
				if (cycleTasks == null || !cycleAcceptsRevivals)
				{
					refusal = $"supervision for '{monitorName}' cannot be resumed because no monitoring cycle is running on host '{Environment.MachineName}'.";
					return null;
				}

				if (cycleToken.IsCancellationRequested || daemonCts.IsCancellationRequested)
				{
					refusal = $"supervision for '{monitorName}' cannot be resumed because the monitoring cycle on host '{Environment.MachineName}' is shutting down.";
					return null;
				}

				var placeholder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				cycleTasks.Add(placeholder.Task);

				refusal = string.Empty;
				Log.Info("Orchestration", $"Holding the monitoring cycle open while supervision for '{monitorName}' is re-established.");
				return new CycleReservation(this, placeholder);
			}
		}

		/// <summary>
		/// Attaches a revived monitor's new monitoring task to the running cycle.
		/// </summary>
		/// <remarks>
		/// Only ever called through a live <see cref="CycleReservation"/>, whose placeholder is still
		/// holding the cycle open, so the list is guaranteed to exist and the cycle is guaranteed not
		/// to have concluded.
		/// </remarks>
		/// <param name="monitoringTask">The revived monitoring loop.</param>
		private void AttachRevivedTask(Task monitoringTask)
		{
			lock (cycleLock)
			{
				// Null only if the cycle tore down while a reservation was outstanding, which the
				// placeholder prevents. Guarded anyway: the task is already running and the monitor
				// is still in activeMonitors, so cleanup will stop its process either way.
				cycleTasks?.Add(monitoringTask);
			}
		}

		/// <summary>
		/// Releases a revival reservation, letting the cycle conclude again once nothing else is
		/// outstanding.
		/// </summary>
		/// <param name="placeholder">The reservation's placeholder.</param>
		private void ReleaseRevivalReservation(TaskCompletionSource placeholder)
		{
			lock (cycleLock)
			{
				cycleTasks?.Remove(placeholder.Task);
			}

			// Completed after the removal so the wait loop, which re-locks when it wakes, never sees
			// a completed placeholder still in the list. Continuations run asynchronously, so this is
			// safe to call whether or not a lock is held.
			placeholder.TrySetResult();
		}

		/// <summary>
		/// Gets whether the current cycle can still supervise. False once it or the daemon has been
		/// cancelled, at which point a revival must not launch anything.
		/// </summary>
		/// <returns>True when supervision is available; otherwise, false.</returns>
		private bool IsSupervisionAvailable()
		{
			if (daemonCts.IsCancellationRequested)
			{
				return false;
			}

			lock (cycleLock)
			{
				return cycleTasks != null && !cycleToken.IsCancellationRequested;
			}
		}

		/// <summary>
		/// A held place in the running monitoring cycle. See <see cref="TryReserveRevival"/>.
		/// </summary>
		private sealed class CycleReservation : ISupervisionReservation
		{
			private readonly DaemonOrchestrator owner;
			private readonly TaskCompletionSource placeholder;

			/// <summary>Guard so a double dispose releases the hold exactly once.</summary>
			private int released;

			/// <summary>
			/// Initializes a new instance of the <see cref="CycleReservation"/> class.
			/// </summary>
			/// <param name="owner">The orchestrator holding the cycle.</param>
			/// <param name="placeholder">The placeholder already added to the cycle's task list.</param>
			internal CycleReservation(DaemonOrchestrator owner, TaskCompletionSource placeholder)
			{
				this.owner = owner;
				this.placeholder = placeholder;
			}

			/// <inheritdoc />
			public bool IsSupervisionAvailable => owner.IsSupervisionAvailable();

			/// <inheritdoc />
			public void Resume(Task monitoringTask)
			{
				ArgumentNullException.ThrowIfNull(monitoringTask);
				owner.AttachRevivedTask(monitoringTask);
			}

			/// <inheritdoc />
			public void Dispose()
			{
				if (Interlocked.Exchange(ref released, 1) != 0)
				{
					return;
				}
				owner.ReleaseRevivalReservation(placeholder);
			}
		}

		/// <summary>
		/// Returns a thread-safe snapshot of active monitor statuses for diagnostics.
		/// Takes a snapshot of the monitors list under lock, then queries status outside the lock
		/// to avoid holding the lock during process I/O (e.g., /proc reads on Linux).
		/// </summary>
		/// <returns>A read-only list of <see cref="HealthMonitorStatus"/> snapshots.</returns>
		public IReadOnlyList<HealthMonitorStatus> GetActiveMonitorStatuses()
		{
			var snapshot = TakeMonitorSnapshot();
			var statuses = new List<HealthMonitorStatus>(snapshot.Count);
			foreach (var monitor in snapshot)
			{
				statuses.Add(monitor.GetStatus());
			}
			return statuses;
		}

		/// <summary>
		/// Signals the daemon to shut down by cancelling the daemon-wide token and then the current
		/// monitoring cycle. See the comment in the body for why that order matters.
		/// </summary>
		/// <remarks>
		/// The cancellation is guarded because this is called from the POSIX signal handler, on a
		/// thread-pool thread, while the headless main path cancels and DISPOSES
		/// <see cref="daemonCts"/> as it winds down. A SIGTERM arriving at the wrong moment reaches
		/// a disposed source and throws <see cref="ObjectDisposedException"/> — unhandled, on a
		/// thread with nobody to catch it, which aborts the process. A supervisor that crashes when
		/// asked to stop leaves systemd unable to tell a clean stop from a failure. Reproduced on a
		/// pristine build in three runs out of five.
		/// </remarks>
		public void Shutdown()
		{
			/* The daemon token goes FIRST, and the order is load-bearing.
			 *
			 * CancellationTokenSource.Cancel runs its registrations synchronously on the calling
			 * thread — here, the signal handler's. Cancelling the cycle first therefore unwinds the
			 * monitoring loops inline, and RunAsync can reach its headless "the cycle ended by
			 * itself" check while this thread is still inside that first Cancel. It then sees a
			 * daemon token that is not cancelled yet, concludes that every monitor exhausted, and
			 * exits 1 — systemd is told an ordinary `stop` failed. Observed once in five SIGTERM
			 * runs, with the log carrying "Headless monitoring cycle completed" two lines after
			 * "SIGTERM received".
			 *
			 * Cancelling the daemon token first makes that impossible: IsCancellationRequested is
			 * set before any registration runs, so whatever unwinds inline afterwards already sees
			 * a daemon that was asked to stop. The cycle is cancelled straight after, which is
			 * belt-and-braces — the cycle's token is linked to this one. */
			try
			{
				daemonCts.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// Already shutting down, which is what this was asking for.
			}

			CancelCurrentMonitoring();
		}


		/// <summary>
		/// Runs the main orchestration loop. Waits for start signals, launches monitors,
		/// and handles stop/shutdown. Returns when the daemon token is cancelled.
		/// In headless mode, automatically initiates daemon shutdown after the monitoring cycle
		/// completes, since no interactive console is available to issue further commands.
		/// </summary>
		/// <returns>A task representing the asynchronous orchestration operation.</returns>
		public async Task RunAsync()
		{
			// Started here so the host is visible to operators from the moment the daemon is up,
			// including while it waits for a 'start' command. It observes the daemon token, so a
			// shutdown stops it with everything else.
			StartControlPlane();

			try
			{
				while (!daemonCts.IsCancellationRequested)
				{
					Log.Info("Orchestration", "Waiting for 'start' command...");

					try
					{
						await startMonitoringSignal.WaitAsync(daemonCts.Token);
					}
					catch (OperationCanceledException)
					{
						Log.Info("Orchestration", "Waiting for start command cancelled. Daemon shutting down.");
						break;
					}

					Log.Info("Orchestration", "'start' command received. Launching application monitors.");
					await RunMonitoringCycleAsync();

					// In headless mode, no interactive console exists to issue further commands.
					// Without this, the daemon would hang forever on the semaphore wait as a zombie.
					if (headless && !daemonCts.IsCancellationRequested)
					{
						Log.Info("Orchestration", "Headless monitoring cycle completed. Initiating automatic daemon shutdown.");
						Volatile.Write(ref headlessCycleCompleted, 1);
						daemonCts.Cancel();
						break;
					}
				}
				Log.Info("Orchestration", "Monitoring orchestration loop exited.");
			}
			catch (OperationCanceledException ex)
			{
				Log.Info("Orchestration", "Monitoring orchestration loop was cancelled by daemon shutdown.", ex);
			}
			catch (Exception ex)
			{
				Log.Critical("Orchestration", $"An unhandled error occurred in the monitoring orchestration loop: {ex.Message}", ex);
				Shutdown();
				throw;
			}
			finally
			{
				await StopControlPlaneAsync();
			}
		}

		/// <summary>
		/// Starts the optional database control plane, if this deployment has one.
		/// </summary>
		/// <remarks>
		/// Every failure path here is a log line and nothing more. A daemon whose database is
		/// missing, misconfigured or unreachable still supervises its applications — that is the
		/// whole point of the control plane being optional.
		/// </remarks>
		private void StartControlPlane()
		{
			if (controlPlane != null)
			{
				return;
			}

			try
			{
				controlPlane = DaemonControlPlane.TryCreate(this, configuration);
				if (controlPlane == null)
				{
					return;
				}

				controlPlaneTask = controlPlane.RunAsync(daemonCts.Token);
			}
			catch (Exception ex)
			{
				controlPlane = null;
				controlPlaneTask = null;
				Log.Error("Orchestration", $"The control plane failed to start: {ex.Message}. Supervision continues without it.", ex);
			}
		}

		/// <summary>
		/// Waits, with a bounded timeout, for the control plane loop to finish after shutdown.
		/// </summary>
		/// <returns>A task representing the asynchronous stop operation.</returns>
		private async Task StopControlPlaneAsync()
		{
			var task = Interlocked.Exchange(ref controlPlaneTask, null);
			if (task == null)
			{
				return;
			}

			try
			{
				await task.WaitAsync(controlPlaneStopTimeout);
			}
			catch (TimeoutException)
			{
				Log.Warning("Orchestration", $"The control plane did not stop within {controlPlaneStopTimeout.TotalSeconds}s. Continuing shutdown.");
			}
			catch (OperationCanceledException)
			{
				// Expected: the loop observes the daemon shutdown token.
			}
			catch (Exception ex)
			{
				Log.Error("Orchestration", $"The control plane loop ended with an error: {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Runs a single monitoring cycle: creates monitors, waits for them to complete or be cancelled,
		/// then cleans up. Owns the per-cycle CTS and completion signal lifecycle.
		/// </summary>
		/// <returns>A task representing the asynchronous monitoring cycle.</returns>
		private async Task RunMonitoringCycleAsync()
		{
			// Write the TCS BEFORE the CTS so concurrent readers (ForceKillAllAsync, DisposeAsync)
			// that see the new CTS always also see the corresponding TCS.
			var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			Volatile.Write(ref cycleCompletionSource, tcs);

			var cycleCts = new CancellationTokenSource();
			Volatile.Write(ref currentMonitoringCts, cycleCts);

			try
			{
				using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cycleCts.Token, daemonCts.Token);

				lock (activeMonitorsLock)
				{
					activeMonitors.Clear();
				}

				// Published before any monitor exists so a revival arriving at any point during setup
				// finds a cycle to join rather than being refused for a race it did not cause.
				var currentMonitoringTasks = new List<Task>();
				lock (cycleLock)
				{
					cycleTasks = currentMonitoringTasks;
					cycleToken = linkedCts.Token;
					cycleAcceptsRevivals = true;
				}

				/* Everything from here on runs against a published cycle, so the publication is
				 * undone on every exit path — including the ones that leave during setup. The
				 * drain loop normally does it the moment it sees the last task go, which is what
				 * makes "the cycle has concluded" and "revivals are refused" the same instant;
				 * this is the net for the paths that never reach the drain. */
				try
				{
					for (int i = 0; i < validatedApps.Count; i++)
					{
						if (linkedCts.Token.IsCancellationRequested)
						{
							Log.Warning("Orchestration", "Monitoring launch cancelled during setup.");
							break;
						}

						var (appConfig, healthCheckers) = validatedApps[i];

						if (linkedCts.Token.IsCancellationRequested)
						{
							Log.Warning("Orchestration", "Monitoring launch cancelled before monitor creation.");
							break;
						}

						Log.Info("Orchestration", $"--- Launching Monitor for: [{appConfig.Name}] ---");

						// 'this' is the supervision host: the seam a monitor uses to ask for its loop back
						// after an operator revives it. See ISupervisionHost.
						var monitor = new HealthMonitor(appConfig, healthCheckers, headless, linkedCts.Token, this);

						if (linkedCts.Token.IsCancellationRequested)
						{
							Log.Warning("Orchestration", "Monitoring launch cancelled after monitor creation.");
							await monitor.DisposeAsync();
							break;
						}

						lock (activeMonitorsLock)
						{
							activeMonitors.Add(monitor);
						}
						currentMonitoringTasks.Add(monitor.StartMonitoringAsync());

						if (appConfig.LaunchDelaySeconds > 0 && i < validatedApps.Count - 1)
						{
							Log.Info("Orchestration", $"Pausing for {appConfig.LaunchDelaySeconds} seconds before starting the next monitor...");
							try
							{
								await Task.Delay(TimeSpan.FromSeconds(appConfig.LaunchDelaySeconds), linkedCts.Token);
							}
							catch (OperationCanceledException)
							{
								Log.Warning("Orchestration", "Launch delay cancelled during monitor setup.");
								break;
							}
						}
					}

					if (currentMonitoringTasks.Count == 0)
					{
						Log.Warning("Orchestration", "No valid applications were launched for monitoring in this cycle.");
						return;
					}

					Log.Info("Orchestration", "All configured application monitors are now active and running.");

					await DrainMonitoringTasksAsync(currentMonitoringTasks);

					Log.Info("Orchestration", "Current monitoring cycle concluded. Initiating cleanup of applications.");
					await CleanupAllMonitorsAsync();
					Log.Info("Orchestration", "Applications cleaned up for this cycle.");
				}
				finally
				{
					ConcludeCycleAcceptance();
				}
			}
			finally
			{
				Interlocked.Exchange(ref currentMonitoringCts, null);
				cycleCts.Dispose();

				// Signal cycle completion AFTER cleanup is done, then clear the reference.
				// This ordering ensures ForceKillAllAsync and DisposeAsync callers wait until cleanup finishes.
				tcs.TrySetResult();
				Volatile.Write(ref cycleCompletionSource, null);
			}
		}

		/// <summary>
		/// Waits for this cycle's monitoring tasks, re-reading the list after every wait so a task
		/// handed back by a revival is waited on like any other.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The list is mutable and <see cref="Task.WhenAll(IEnumerable{Task})"/> is not — it waits on
		/// exactly what it was handed. A single WhenAll over the starting set would therefore let the
		/// cycle conclude, and a headless daemon exit, on top of an application an operator had just
		/// revived: the revived task would have been added to a list nobody was reading any more.
		/// </para>
		/// <para>
		/// So the list is drained instead, under <see cref="cycleLock"/>, and the step that observes
		/// it empty is the same step that stops accepting revivals. A revival therefore either gets
		/// its placeholder in while the cycle is still live — and the cycle then waits for it — or is
		/// refused outright. There is no ordering in which one is accepted and then dropped.
		/// </para>
		/// </remarks>
		/// <param name="monitoringTasks">The cycle's task list. Mutated by revivals under <see cref="cycleLock"/>.</param>
		/// <returns>A task that completes when the cycle has nothing left to wait on.</returns>
		private async Task DrainMonitoringTasksAsync(List<Task> monitoringTasks)
		{
			while (true)
			{
				Task[] pending;
				lock (cycleLock)
				{
					if (monitoringTasks.Count == 0)
					{
						// Same lock, same step as the observation above: from here on a revival is
						// refused rather than joining a cycle that is about to clean up.
						ConcludeCycleAcceptance();
						Log.Debug("Orchestration", "All monitoring tasks for this cycle have completed.");
						return;
					}

					pending = monitoringTasks.ToArray();
				}

				try
				{
					await Task.WhenAll(pending);
				}
				catch (Exception ex)
				{
					// Inspect every task individually — Task.WhenAll only throws the first exception.
					// Log the aggregate exception as a fallback in case individual inspection misses anything.
					Log.Debug("Orchestration", $"Task.WhenAll threw: {ex.Message}", ex);
					foreach (var task in pending)
					{
						if (task.IsCanceled)
						{
							Log.Info("Orchestration", "A monitoring task was cancelled (e.g., by 'stop' or daemon shutdown).");
						}
						else if (task.IsFaulted && task.Exception != null)
						{
							foreach (var innerEx in task.Exception.InnerExceptions)
							{
								Log.Error("Orchestration", $"Monitor task faulted: {innerEx.Message}", innerEx);
							}
						}
					}
				}

				lock (cycleLock)
				{
					// Only the finished ones: a revival added while the wait was in flight stays,
					// and the next pass waits on it.
					monitoringTasks.RemoveAll(static task => task.IsCompleted);
				}
			}
		}

		/// <summary>
		/// Stops the current cycle from accepting revivals and unpublishes it.
		/// </summary>
		/// <remarks>
		/// Idempotent, and safe to call while already holding <see cref="cycleLock"/>. Clearing
		/// <see cref="cycleTasks"/> is what refuses later revivals; <see cref="cycleToken"/> is
		/// dropped with it so no reader can touch a token whose source is about to be disposed.
		/// </remarks>
		private void ConcludeCycleAcceptance()
		{
			lock (cycleLock)
			{
				cycleAcceptsRevivals = false;
				cycleTasks = null;
				cycleToken = default;
			}
		}

		/// <summary>
		/// Kills and disposes all active monitors, then clears the list.
		/// </summary>
		/// <returns>A task representing the asynchronous cleanup operation.</returns>
		private async Task CleanupAllMonitorsAsync()
		{
			HealthMonitor[] snapshot;
			lock (activeMonitorsLock)
			{
				snapshot = activeMonitors.ToArray();
				activeMonitors.Clear();
			}

			if (snapshot.Length == 0)
			{
				return;
			}

			var tasks = new Task[snapshot.Length];
			for (int i = 0; i < snapshot.Length; i++)
			{
				tasks[i] = DisposeMonitorSafeAsync(snapshot[i]);
			}
			await Task.WhenAll(tasks);
		}

		/// <summary>
		/// Disposes a single monitor, catching and logging any exceptions.
		/// </summary>
		/// <param name="monitor">The monitor to dispose.</param>
		/// <returns>A task representing the asynchronous dispose operation.</returns>
		private static async Task DisposeMonitorSafeAsync(HealthMonitor monitor)
		{
			try
			{
				await monitor.DisposeAsync();
			}
			catch (Exception ex)
			{
				Log.Error("Orchestration", $"Error disposing monitor '{monitor.Name}': {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Disposes of orchestrator resources, cleaning up any remaining active monitors,
		/// health checkers, and CTS instances.
		/// </summary>
		/// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
		public async ValueTask DisposeAsync()
		{
			if (Interlocked.CompareExchange(ref isDisposed, 1, 0) != 0)
			{
				return;
			}
			GC.SuppressFinalize(this);

			// Capture the TCS BEFORE cancelling so we have a stable reference.
			// The cycle's finally block nulls cycleCompletionSource after completion,
			// so reading after Cancel() could race and see null.
			var tcs = Volatile.Read(ref cycleCompletionSource);

			daemonCts.Cancel();

			// The control plane observes the daemon token, so this only waits for its loop to unwind.
			await StopControlPlaneAsync();

			// Re-read after cancellation to catch any cycle that started in the race window
			// between the initial TCS capture and the Cancel() call above.
			tcs ??= Volatile.Read(ref cycleCompletionSource);

			// Await the active monitoring cycle so its cleanup completes before we dispose monitors again.
			// Use a bounded timeout to prevent hanging indefinitely on stuck cycles.
			// The delay CTS ensures the timer is cancelled immediately when the cycle completes,
			// avoiding an orphaned 30-second timer running in the background.
			if (tcs != null)
			{
				using var delayCts = new CancellationTokenSource();
				var delayTask = Task.Delay(disposeTimeout, delayCts.Token);
				var completed = await Task.WhenAny(tcs.Task, delayTask);
				if (completed == tcs.Task)
				{
					await delayCts.CancelAsync();
				}
				else
				{
					Log.Warning("Orchestration", $"Monitoring cycle did not complete within {disposeTimeout.TotalSeconds}s during disposal. Proceeding with cleanup.");
				}
			}

			await CleanupAllMonitorsAsync();

			var remainingCts = Interlocked.Exchange(ref currentMonitoringCts, null);
			remainingCts?.Dispose();

			daemonCts.Dispose();
			startMonitoringSignal.Dispose();
		}
	}
}