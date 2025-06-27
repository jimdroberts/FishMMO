using Microsoft.Extensions.Configuration;

namespace AppHealthMonitor
{
	class Program
	{
		// ManualResetEventSlim to signal when monitoring should start/restart
		private static ManualResetEventSlim startMonitoringEvent = new ManualResetEventSlim(false);

		// CancellationTokenSource for the *current monitoring cycle*.
		// This allows 'stop' to cancel only the active monitoring without shutting down the daemon.
		private static CancellationTokenSource currentMonitoringCts;

		// List to keep track of HealthMonitor instances for explicit cleanup
		private static List<HealthMonitor> activeMonitors = new List<HealthMonitor>();

		/// <summary>
		/// Helper method for consistent logging to the console.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="color">Optional. The foreground color for the message.</param>
		private static void Log(string message, ConsoleColor? color = null)
		{
			if (color.HasValue)
			{
				Console.ForegroundColor = color.Value;
			}
			Console.WriteLine(message);
			Console.ResetColor(); // Always reset color after writing
		}


		static async Task Main(string[] args)
		{
			Log("Starting Application Health Monitor Daemon...");

			// --- Load Configuration from appsettings.json ---
			var builder = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

			IConfiguration configuration = builder.Build();

			var appConfigs = configuration.GetSection("Applications").Get<List<AppConfig>>();

			if (appConfigs == null || appConfigs.Count == 0)
			{
				Log("Error: No application configurations found in 'Applications' section of appsettings.json. Please configure at least one application.", ConsoleColor.Red);
				return;
			}

			// Central CancellationTokenSource for the entire daemon's lifetime (Ctrl+C, or final shutdown command)
			using (var sharedDaemonCts = new CancellationTokenSource())
			{
				// Register a single handler for Ctrl+C to cancel the shared token source
				Console.CancelKeyPress += (sender, eventArgs) =>
				{
					if (!sharedDaemonCts.IsCancellationRequested)
					{
						Log("\nCtrl+C pressed. Signalling daemon shutdown...", ConsoleColor.Cyan);
						sharedDaemonCts.Cancel();
						eventArgs.Cancel = true; // Prevent immediate termination
					}
				};

				// The main orchestration loop that manages start/stop of application monitors
				Task orchestrationLoopTask = RunMonitoringOrchestrationLoop(appConfigs, sharedDaemonCts.Token);

				// Start the console command reader in parallel
				Task consoleReaderTask = ConsoleCommandReader(sharedDaemonCts, startMonitoringEvent);

				Log("\nApplication Health Monitor Daemon is ready.", ConsoleColor.Green);
				Log("Type 'help' to list available commands.", ConsoleColor.Green);

				// Wait for the main orchestration loop and console reader to complete (when daemon-wide shutdown is requested)
				await Task.WhenAll(orchestrationLoopTask, consoleReaderTask);

				// Final cleanup: ensure all processes are terminated when the daemon itself is stopping.
				// This handles cases where 'stop'/'force-kill' might not have been called, but 'shutdown'/'exit' was.
				Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] All daemon tasks have concluded. Initiating final cleanup of all monitored applications.", ConsoleColor.Yellow);

				foreach (var monitor in activeMonitors.ToList()) // ToList to ensure collection isn't modified during enumeration if it's cleared elsewhere
				{
					monitor.KillApplication(); // This method already handles if the process is null or exited.
				}
				activeMonitors.Clear(); // Clear the list after cleanup

				Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] All monitored applications terminated. Application Health Monitor Daemon stopped gracefully.", ConsoleColor.Yellow);
			}
		}

		/// <summary>
		/// This method contains the main loop that orchestrates starting and stopping monitoring cycles.
		/// It waits for the 'start' signal, launches monitors, and then waits for a 'stop' or daemon shutdown signal.
		/// </summary>
		/// <param name="appConfigs">List of application configurations.</param>
		/// <param name="daemonCancellationToken">Cancellation token for overall daemon shutdown.</param>
		static async Task RunMonitoringOrchestrationLoop(
			List<AppConfig> appConfigs,
			CancellationToken daemonCancellationToken)
		{
			try
			{
				while (!daemonCancellationToken.IsCancellationRequested)
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Waiting for 'start' command...", ConsoleColor.DarkGray);

					// This will block until startMonitoringEvent.Set() is called.
					try
					{
						await Task.Run(() => startMonitoringEvent.Wait(daemonCancellationToken), daemonCancellationToken);
					}
					catch (OperationCanceledException)
					{
						// Daemon is shutting down, exit loop
						Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Waiting for start command cancelled. Daemon shutting down.");
						break;
					}

					// IMPORTANT: Reset the event immediately after it has been set AND consumed by the wait.
					// This ensures that the event is always ready to receive a new Set() signal for the next monitoring cycle.
					startMonitoringEvent.Reset();

					Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] 'start' command received. Launching application monitors...", ConsoleColor.Green);

					using (currentMonitoringCts = new CancellationTokenSource())
					using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(currentMonitoringCts.Token, daemonCancellationToken))
					{
						activeMonitors.Clear();
						List<Task> currentMonitoringTasks = new List<Task>();

						for (int i = 0; i < appConfigs.Count; i++)
						{
							if (linkedCts.Token.IsCancellationRequested)
							{
								Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Monitoring launch cancelled during setup.", ConsoleColor.Yellow);
								break;
							}

							var appConfig = appConfigs[i];

							if (string.IsNullOrWhiteSpace(appConfig.Name) ||
								string.IsNullOrWhiteSpace(appConfig.ApplicationExePath))
							{
								Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Skipping invalid configuration entry during launch: Name='{appConfig.Name}' ExePath='{appConfig.ApplicationExePath}'.", ConsoleColor.Yellow);
								continue;
							}

							if (appConfig.PortTypes == null || !appConfig.PortTypes.Any())
							{
								appConfig.PortTypes = new List<PortType> { PortType.None };
							}

							TimeSpan checkInterval = TimeSpan.FromSeconds(appConfig.CheckIntervalSeconds > 0 ? appConfig.CheckIntervalSeconds : 10);

							Log($"\n--- Launching Monitor for: [{appConfig.Name}] ---");
							Log($"    ApplicationExePath: {appConfig.ApplicationExePath}");
							if (appConfig.PortTypes.Count == 1 && appConfig.PortTypes.Contains(PortType.None))
							{
								Log($"    Monitoring Mode: Process Only (No Port Checks)");
							}
							else
							{
								Log($"    MonitoredPort: {appConfig.MonitoredPort} (Types: {string.Join(", ", appConfig.PortTypes)})");
							}
							Log($"    LaunchArguments: {appConfig.LaunchArguments}");
							Log($"    CheckInterval: {checkInterval.TotalSeconds} seconds");
							Log($"    LaunchDelay: {appConfig.LaunchDelaySeconds} seconds");
							Log($"    CPU Threshold: {appConfig.CpuThresholdPercent}%");
							Log($"    Memory Threshold: {appConfig.MemoryThresholdMB}MB");
							Log($"    Graceful Shutdown Timeout: {appConfig.GracefulShutdownTimeoutSeconds} seconds");
							Log($"    Restart Backoff (Initial/Max Attempts/Max Delay): {appConfig.InitialRestartDelaySeconds}s / {appConfig.MaxRestartAttempts} / {appConfig.MaxRestartDelaySeconds}s");
							Log($"    Circuit Breaker (Failures/Reset Timeout): {appConfig.CircuitBreakerFailureThreshold} / {appConfig.CircuitBreakerResetTimeoutMinutes}min");

							HealthMonitor monitor = new HealthMonitor(
								appConfig.ApplicationExePath,
								appConfig.MonitoredPort,
								appConfig.PortTypes,
								appConfig.LaunchArguments,
								checkInterval,
								appConfig.CpuThresholdPercent,
								appConfig.MemoryThresholdMB,
								appConfig.GracefulShutdownTimeoutSeconds,
								appConfig.InitialRestartDelaySeconds,
								appConfig.MaxRestartDelaySeconds,
								appConfig.MaxRestartAttempts,
								appConfig.CircuitBreakerFailureThreshold,
								appConfig.CircuitBreakerResetTimeoutMinutes,
								linkedCts.Token
							);

							activeMonitors.Add(monitor);
							currentMonitoringTasks.Add(monitor.StartMonitoring());

							if (appConfig.LaunchDelaySeconds > 0 && i < appConfigs.Count - 1)
							{
								Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Pausing for {appConfig.LaunchDelaySeconds} seconds before starting the next monitor...", ConsoleColor.Cyan);
								try
								{
									await Task.Delay(TimeSpan.FromSeconds(appConfig.LaunchDelaySeconds), linkedCts.Token);
								}
								catch (OperationCanceledException)
								{
									Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Launch delay cancelled during monitor setup.", ConsoleColor.Yellow);
									break;
								}
							}
						}

						if (!currentMonitoringTasks.Any())
						{
							Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] No valid applications were launched for monitoring in this cycle.", ConsoleColor.Yellow);
							continue;
						}
						else
						{
							Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] All configured application monitors are now active and running.", ConsoleColor.Green);
						}

						// Wait for all current monitoring tasks to complete
						try
						{
							Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Waiting for current monitoring tasks to complete or be cancelled...");
							await Task.WhenAll(currentMonitoringTasks);
							Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] All current monitoring tasks completed normally.");
						}
						catch (OperationCanceledException)
						{
							Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] One or more monitoring tasks were cancelled (e.g., by 'stop' or daemon shutdown).");
						}
					} // currentMonitoringCts is disposed here

					Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Current monitoring cycle concluded. Initiating cleanup of applications.", ConsoleColor.Yellow);

					foreach (var monitor in activeMonitors.ToList())
					{
						Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Calling KillApplication for a monitor.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Applications cleaned up for this cycle.", ConsoleColor.Yellow);
				}
				Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Monitoring orchestration loop exited.");
			}
			catch (OperationCanceledException)
			{
				Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Monitoring orchestration loop was cancelled by daemon shutdown.");
			}
			catch (Exception ex)
			{
				Log($"[{DateTime.Now:HH:mm:ss}] [Orchestration] An unhandled error occurred in the monitoring orchestration loop: {ex.Message}", ConsoleColor.Red);
			}
		}

		/// <summary>
		/// Reads console input and triggers daemon actions based on commands.
		/// </summary>
		/// <param name="sharedDaemonCts">The shared CancellationTokenSource for overall daemon shutdown.</param>
		/// <param name="startEvent">ManualResetEventSlim to signal the start of monitoring.</param>
		static async Task ConsoleCommandReader(CancellationTokenSource sharedDaemonCts, ManualResetEventSlim startEvent)
		{
			var commands = new Dictionary<string, ConsoleCommand>();

			bool IsMonitoringActive() => currentMonitoringCts != null && !currentMonitoringCts.IsCancellationRequested;

			commands.Add("help", new ConsoleCommand("help", "Lists all available commands.", async () =>
			{
				Log("\n--- Available Commands ---", ConsoleColor.Yellow);
				foreach (var cmd in commands.Values.OrderBy(c => c.Name))
				{
					Log($"  {cmd.Name,-15} - {cmd.Description}");
				}
				Log("--------------------------", ConsoleColor.Yellow);
				await Task.CompletedTask;
			}));

			commands.Add("start", new ConsoleCommand("start", "Starts monitoring all configured applications.", async () =>
			{
				if (IsMonitoringActive())
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Monitoring is already active.", ConsoleColor.Yellow);
				}
				else
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'start' command received. Signalling monitoring to begin...", ConsoleColor.Cyan);
					startEvent.Set();
				}
				await Task.CompletedTask;
			}));

			commands.Add("stop", new ConsoleCommand("stop", "Gracefully terminates monitored applications and returns to waiting state.", async () =>
			{
				if (IsMonitoringActive())
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'stop' command received. Cancelling current monitoring cycle (currentMonitoringCts)...", ConsoleColor.Cyan);
					currentMonitoringCts.Cancel();
				}
				else
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Monitoring is not active, or already stopping.", ConsoleColor.Yellow);
				}
				await Task.CompletedTask;
			}));

			commands.Add("force-kill", new ConsoleCommand("force-kill", "Immediately terminates all monitored applications, bypassing graceful shutdown.", async () =>
			{
				if (IsMonitoringActive())
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'force-kill' command received. Immediately terminating all monitored processes...", ConsoleColor.Red);

					foreach (var monitor in activeMonitors.ToList())
					{
						Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Force-killing monitor process directly.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					currentMonitoringCts.Cancel();
					startEvent.Reset();
				}
				else
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] No active monitoring to force-kill.", ConsoleColor.Yellow);
				}
				await Task.CompletedTask;
			}));

			commands.Add("force-restart", new ConsoleCommand("force-restart", "Immediately terminates and then restarts all applications.", async () =>
			{
				if (IsMonitoringActive())
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'force-restart' command received. Immediately terminating and then restarting all monitored processes...", ConsoleColor.Red);

					foreach (var monitor in activeMonitors.ToList())
					{
						Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Force-killing monitor process directly for restart.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					currentMonitoringCts.Cancel();
					startEvent.Set();
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Restart sequence initiated. Applications will re-launch shortly.", ConsoleColor.Green);
				}
				else if (!startEvent.IsSet)
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Monitoring is not active. Signalling 'start' to launch applications for force-restart.", ConsoleColor.Yellow);
					startEvent.Set();
				}
				else
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Cannot force-restart: Monitoring cycle is already stopping or in an unexpected state.", ConsoleColor.Yellow);
				}
				await Task.CompletedTask;
			}));

			commands.Add("shutdown", new ConsoleCommand("shutdown", "Gracefully stops the daemon and all monitored applications.", async () =>
			{
				Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'shutdown' command received. Initiating graceful daemon shutdown...", ConsoleColor.Cyan);

				if (currentMonitoringCts != null && !currentMonitoringCts.IsCancellationRequested)
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Signalled active monitoring cycle to stop first.");
					currentMonitoringCts.Cancel();
				}
				sharedDaemonCts.Cancel();
				await Task.CompletedTask;
			}));

			commands.Add("exit", new ConsoleCommand("exit", "Alias for 'shutdown'.", async () =>
			{
				await commands["shutdown"].Action();
			}));

			// Main loop for reading commands
			while (!sharedDaemonCts.IsCancellationRequested)
			{
				// Print the prompt on the same line as input
				Console.Write("Daemon Command > ");
				string input = await Task.Run(() => Console.ReadLine()?.ToLowerInvariant(), sharedDaemonCts.Token)
										 .ContinueWith(t => t.IsCanceled ? null : t.Result, sharedDaemonCts.Token);

				if (sharedDaemonCts.IsCancellationRequested)
				{
					break;
				}

				if (string.IsNullOrWhiteSpace(input))
				{
					continue;
				}

				if (commands.TryGetValue(input, out var command))
				{
					await command.Action();
				}
				else
				{
					Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Unknown command: '{input}'. Type 'help' to see available commands.", ConsoleColor.Yellow);
				}
			}
			Log($"[{DateTime.Now:HH:mm:ss}] [Daemon] Console command reader stopped.");
		}
	}
}