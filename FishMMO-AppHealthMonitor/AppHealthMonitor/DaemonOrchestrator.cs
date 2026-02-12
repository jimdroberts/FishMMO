using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Orchestrates the lifecycle of application health monitors.
	/// Owns daemon-wide state (cancellation, start signal, active monitors)
	/// and pre-validates configurations at construction time.
	/// </summary>
	public sealed class DaemonOrchestrator : IAsyncDisposable
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
		/// <exception cref="InvalidOperationException">Thrown when no valid configurations remain after validation, or when duplicate application names are detected.</exception>
		public DaemonOrchestrator(IReadOnlyList<AppConfig> appConfigs, bool headless)
		{
			ArgumentNullException.ThrowIfNull(appConfigs);

			var apps = new List<(AppConfig, IReadOnlyList<IHealthChecker>)>(appConfigs.Count);
			var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var appConfig in appConfigs)
			{
				if (!appConfig.TryApplyDefaultsAndValidate(out string error))
				{
					Log.Warning("Orchestration", $"Skipping invalid configuration: {error}");
					continue;
				}

				if (!seenNames.Add(appConfig.Name))
				{
					throw new InvalidOperationException($"Duplicate application name '{appConfig.Name}'. Each application must have a unique Name.");
				}

				var healthCheckers = HealthCheckerFactory.Create(appConfig.PortTypes);
				apps.Add((appConfig, healthCheckers));

				LogAppConfiguration(appConfig);
			}

			if (apps.Count == 0)
			{
				throw new InvalidOperationException("No valid application configurations found after validation.");
			}

			validatedApps = apps;

			this.headless = headless;

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
				{ "WebSocketCheckTimeout", $"{appConfig.WebSocketCheckTimeoutMs}ms" },
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
				await Task.WhenAll(tasks);
			}

			return capturedCycleCompletion;
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
		/// Signals the daemon to shut down by cancelling monitoring and the daemon-wide token.
		/// <see cref="CancelCurrentMonitoring"/> is called defensively before <see cref="daemonCts"/>.
		/// The linked CTS in <see cref="RunAsync"/> ensures propagation either way.
		/// </summary>
		public void Shutdown()
		{
			CancelCurrentMonitoring();
			daemonCts.Cancel();
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

				var currentMonitoringTasks = new List<Task>();

				for (int i = 0; i < validatedApps.Count; i++)
				{
					if (linkedCts.Token.IsCancellationRequested)
					{
						Log.Warning("Orchestration", "Monitoring launch cancelled during setup.");
						break;
					}

					var (appConfig, healthCheckers) = validatedApps[i];

					Log.Info("Orchestration", $"--- Launching Monitor for: [{appConfig.Name}] ---");

					var monitor = new HealthMonitor(appConfig, healthCheckers, headless, linkedCts.Token);

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

				try
				{
					await Task.WhenAll(currentMonitoringTasks);
					Log.Debug("Orchestration", "All current monitoring tasks completed normally.");
				}
				catch (Exception ex)
				{
					// Inspect every task individually — Task.WhenAll only throws the first exception.
					// Log the aggregate exception as a fallback in case individual inspection misses anything.
					Log.Debug("Orchestration", $"Task.WhenAll threw: {ex.Message}", ex);
					foreach (var task in currentMonitoringTasks)
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

				Log.Info("Orchestration", "Current monitoring cycle concluded. Initiating cleanup of applications.");
				await CleanupAllMonitorsAsync();
				Log.Info("Orchestration", "Applications cleaned up for this cycle.");
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