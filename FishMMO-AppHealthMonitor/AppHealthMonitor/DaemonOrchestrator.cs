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
		private readonly SemaphoreSlim startMonitoringSignal = new SemaphoreSlim(0, 1);
		private readonly object activeMonitorsLock = new object();
		private readonly List<HealthMonitor> activeMonitors = new List<HealthMonitor>();
		private readonly IReadOnlyList<(AppConfig Config, IReadOnlyList<IHealthChecker> HealthCheckers)> validatedApps;
		private readonly CancellationTokenSource daemonCts = new CancellationTokenSource();
		private TaskCompletionSource? cycleCompletionSource;
		private CancellationTokenSource? currentMonitoringCts;
		private int disposed;

		/// <summary>
		/// Gets whether the daemon has been signalled to shut down.
		/// </summary>
		public bool IsDaemonShutdownRequested => daemonCts.IsCancellationRequested;

		/// <summary>
		/// Initializes a new instance of the <see cref="DaemonOrchestrator"/> class.
		/// Applies defaults, validates, creates health checkers, and logs configuration for each app.
		/// </summary>
		/// <param name="appConfigs">The raw application configurations from settings.</param>
		/// <exception cref="InvalidOperationException">Thrown when no valid configurations remain after validation.</exception>
		public DaemonOrchestrator(List<AppConfig> appConfigs)
		{
			var apps = new List<(AppConfig, IReadOnlyList<IHealthChecker>)>(appConfigs.Count);

			foreach (var appConfig in appConfigs)
			{
				if (!appConfig.TryApplyDefaultsAndValidate(out string error))
				{
					Log.Warning("Orchestration", $"Skipping invalid configuration: {error}");
					continue;
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
				{ "Headless", appConfig.Headless },
				{ "CheckInterval", $"{appConfig.CheckIntervalSeconds}s" },
				{ "LaunchDelay", $"{appConfig.LaunchDelaySeconds}s" },
				{ "CpuThreshold", $"{appConfig.CpuThresholdPercent}%" },
				{ "MemoryThreshold", $"{appConfig.MemoryThresholdMB}MB" },
				{ "GracefulShutdownTimeout", $"{appConfig.GracefulShutdownTimeoutSeconds}s" },
				{ "ForceKillTimeout", $"{appConfig.ForceKillTimeoutSeconds}s" },
				{ "InitialHealthCheckDelay", $"{appConfig.InitialHealthCheckDelaySeconds}s" },
				{ "PostLaunchSettleDelay", $"{appConfig.PostLaunchSettleDelaySeconds}s" },
				{ "InitialRestartDelay", $"{appConfig.InitialRestartDelaySeconds}s" },
				{ "MaxRestartDelay", $"{appConfig.MaxRestartDelaySeconds}s" },
				{ "MaxRestartAttempts", appConfig.MaxRestartAttempts },
				{ "PortCheckTimeout", $"{appConfig.PortCheckTimeoutMs}ms" },
				{ "WebSocketCheckTimeout", $"{appConfig.WebSocketCheckTimeoutMs}ms" },
				{ "CircuitBreakerFailureThreshold", appConfig.CircuitBreakerFailureThreshold },
				{ "CircuitBreakerResetTimeout", $"{appConfig.CircuitBreakerResetTimeoutMinutes}min" }
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
		}

		/// <summary>
		/// Thread-safe cancellation of the current monitoring cycle.
		/// Uses <see cref="Volatile.Read"/> for lock-free access.
		/// </summary>
		public void CancelCurrentMonitoring()
		{
			Volatile.Read(ref currentMonitoringCts)?.Cancel();
		}

		/// <summary>
		/// Cancels monitoring and force-kills all active monitored processes.
		/// Returns only after all processes have been terminated.
		/// Thread-safe: takes a snapshot of active monitors under lock before killing.
		/// </summary>
		/// <returns>A task representing the asynchronous force-kill operation.</returns>
		public async Task ForceKillAllAsync()
		{
			CancelCurrentMonitoring();

			List<HealthMonitor> snapshot;
			lock (activeMonitorsLock)
			{
				snapshot = new List<HealthMonitor>(activeMonitors);
			}

			if (snapshot.Count > 0)
			{
				var tasks = new Task[snapshot.Count];
				for (int i = 0; i < snapshot.Count; i++)
				{
					tasks[i] = snapshot[i].KillApplicationAsync();
				}
				await Task.WhenAll(tasks);
			}
		}

		/// <summary>
		/// Waits for the current monitoring cycle to fully complete cleanup.
		/// Used by force-restart to ensure the old cycle has finished before starting a new one.
		/// Returns immediately if no cycle is active.
		/// </summary>
		/// <returns>A task representing the asynchronous wait operation.</returns>
		public async Task AwaitCycleCompletionAsync()
		{
			var tcs = Volatile.Read(ref cycleCompletionSource);
			if (tcs != null)
			{
				await tcs.Task;
			}
		}

		/// <summary>
		/// Returns a thread-safe snapshot of active monitor statuses for diagnostics.
		/// </summary>
		/// <returns>A list of <see cref="HealthMonitorStatus"/> snapshots.</returns>
		public List<HealthMonitorStatus> GetActiveMonitorStatuses()
		{
			lock (activeMonitorsLock)
			{
				var statuses = new List<HealthMonitorStatus>(activeMonitors.Count);
				foreach (var monitor in activeMonitors)
				{
					statuses.Add(monitor.GetStatus());
				}
				return statuses;
			}
		}

		/// <summary>
		/// Signals the daemon to shut down by cancelling monitoring and the daemon-wide token.
		/// </summary>
		public void Shutdown()
		{
			CancelCurrentMonitoring();
			if (!daemonCts.IsCancellationRequested)
			{
				daemonCts.Cancel();
			}
		}

		/// <summary>
		/// Runs the main orchestration loop. Waits for start signals, launches monitors,
		/// and handles stop/shutdown. Returns when the daemon token is cancelled.
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

					var cycleCts = new CancellationTokenSource();
					Volatile.Write(ref currentMonitoringCts, cycleCts);

					var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					Volatile.Write(ref cycleCompletionSource, tcs);

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

							var monitor = new HealthMonitor(appConfig, healthCheckers, linkedCts.Token);

							lock (activeMonitorsLock)
							{
								activeMonitors.Add(monitor);
							}
							currentMonitoringTasks.Add(monitor.StartMonitoring());

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
							continue;
						}

						Log.Info("Orchestration", "All configured application monitors are now active and running.");

						try
						{
							await Task.WhenAll(currentMonitoringTasks);
							Log.Debug("Orchestration", "All current monitoring tasks completed normally.");
						}
						catch (OperationCanceledException)
						{
							Log.Info("Orchestration", "One or more monitoring tasks were cancelled (e.g., by 'stop' or daemon shutdown).");
						}

						Log.Warning("Orchestration", "Current monitoring cycle concluded. Initiating cleanup of applications.");
						await CleanupAllMonitorsAsync();
						Log.Warning("Orchestration", "Applications cleaned up for this cycle.");
					}
					finally
					{
						Interlocked.Exchange(ref currentMonitoringCts, null);
						cycleCts.Dispose();
						tcs.TrySetResult();
						Volatile.Write(ref cycleCompletionSource, null);
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
		/// Kills and disposes all active monitors, then clears the list.
		/// </summary>
		/// <returns>A task representing the asynchronous cleanup operation.</returns>
		private async Task CleanupAllMonitorsAsync()
		{
			List<HealthMonitor> snapshot;
			lock (activeMonitorsLock)
			{
				snapshot = new List<HealthMonitor>(activeMonitors);
				activeMonitors.Clear();
			}

			if (snapshot.Count == 0)
			{
				return;
			}

			var tasks = new Task[snapshot.Count];
			for (int i = 0; i < snapshot.Count; i++)
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
				Log.Error("Orchestration", $"Error disposing monitor: {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Disposes of orchestrator resources, cleaning up any remaining active monitors and CTS instances.
		/// </summary>
		/// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
		public async ValueTask DisposeAsync()
		{
			if (Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
			{
				return;
			}

			if (!daemonCts.IsCancellationRequested)
			{
				daemonCts.Cancel();
			}

			await CleanupAllMonitorsAsync();

			var remainingCts = Interlocked.Exchange(ref currentMonitoringCts, null);
			remainingCts?.Dispose();

			daemonCts.Dispose();
			startMonitoringSignal.Dispose();
		}
	}
}