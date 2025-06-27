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

		static async Task Main(string[] args)
		{
			Console.WriteLine("Starting Application Health Monitor Daemon...");

			// --- Load Configuration from appsettings.json ---
			var builder = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

			IConfiguration configuration = builder.Build();

			var appConfigs = configuration.GetSection("Applications").Get<List<AppConfig>>();

			if (appConfigs == null || appConfigs.Count == 0)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Error: No application configurations found in 'Applications' section of appsettings.json. Please configure at least one application.");
				Console.ResetColor();
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
						Console.WriteLine("\nCtrl+C pressed. Signalling daemon shutdown...");
						sharedDaemonCts.Cancel();
						eventArgs.Cancel = true; // Prevent immediate termination
					}
				};

				// The main orchestration loop that manages start/stop of application monitors
				Task orchestrationLoopTask = RunMonitoringOrchestrationLoop(appConfigs, sharedDaemonCts.Token);

				// Start the console command reader in parallel
				Task consoleReaderTask = ConsoleCommandReader(sharedDaemonCts, startMonitoringEvent);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine("\nApplication Health Monitor Daemon is ready.");
				Console.WriteLine("Type 'help' to list available commands.");
				Console.ResetColor();

				// Wait for the main orchestration loop and console reader to complete (when daemon-wide shutdown is requested)
				await Task.WhenAll(orchestrationLoopTask, consoleReaderTask);

				// Final cleanup: ensure all processes are terminated when the daemon itself is stopping.
				// This handles cases where 'stop'/'force-kill' might not have been called, but 'shutdown'/'exit' was.
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] All daemon tasks have concluded. Initiating final cleanup of all monitored applications.");
				Console.ResetColor();

				foreach (var monitor in activeMonitors.ToList()) // ToList to ensure collection isn't modified during enumeration if it's cleared elsewhere
				{
					monitor.KillApplication(); // This method already handles if the process is null or exited.
				}
				activeMonitors.Clear(); // Clear the list after cleanup

				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] All monitored applications terminated. Application Health Monitor Daemon stopped gracefully.");
				Console.ResetColor();
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
					Console.ForegroundColor = ConsoleColor.DarkGray;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Waiting for 'start' command...");
					Console.ResetColor();

					// This will block until startMonitoringEvent.Set() is called.
					try
					{
						await Task.Run(() => startMonitoringEvent.Wait(daemonCancellationToken), daemonCancellationToken);
					}
					catch (OperationCanceledException)
					{
						// Daemon is shutting down, exit loop
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Waiting for start command cancelled. Daemon shutting down.");
						break;
					}

					// IMPORTANT: Reset the event immediately after it has been set AND consumed by the wait.
					// This ensures that the event is always ready to receive a new Set() signal for the next monitoring cycle.
					startMonitoringEvent.Reset();

					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] 'start' command received. Launching application monitors...");
					Console.ResetColor();

					using (currentMonitoringCts = new CancellationTokenSource())
					using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(currentMonitoringCts.Token, daemonCancellationToken))
					{
						activeMonitors.Clear();
						List<Task> currentMonitoringTasks = new List<Task>();

						for (int i = 0; i < appConfigs.Count; i++)
						{
							if (linkedCts.Token.IsCancellationRequested)
							{
								Console.ForegroundColor = ConsoleColor.Yellow;
								Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Monitoring launch cancelled during setup.");
								Console.ResetColor();
								break;
							}

							var appConfig = appConfigs[i];

							if (string.IsNullOrWhiteSpace(appConfig.Name) ||
								string.IsNullOrWhiteSpace(appConfig.ApplicationExePath))
							{
								Console.ForegroundColor = ConsoleColor.Yellow;
								Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Skipping invalid configuration entry during launch: Name='{appConfig.Name}' ExePath='{appConfig.ApplicationExePath}'.");
								Console.ResetColor();
								continue;
							}

							if (appConfig.PortTypes == null || !appConfig.PortTypes.Any())
							{
								appConfig.PortTypes = new List<PortType> { PortType.None };
							}

							TimeSpan checkInterval = TimeSpan.FromSeconds(appConfig.CheckIntervalSeconds > 0 ? appConfig.CheckIntervalSeconds : 10);

							Console.WriteLine($"\n--- Launching Monitor for: [{appConfig.Name}] ---");
							Console.WriteLine($"    ApplicationExePath: {appConfig.ApplicationExePath}");
							if (appConfig.PortTypes.Count == 1 && appConfig.PortTypes.Contains(PortType.None))
							{
								Console.WriteLine($"    Monitoring Mode: Process Only (No Port Checks)");
							}
							else
							{
								Console.WriteLine($"    MonitoredPort: {appConfig.MonitoredPort} (Types: {string.Join(", ", appConfig.PortTypes)})");
							}
							Console.WriteLine($"    LaunchArguments: {appConfig.LaunchArguments}");
							Console.WriteLine($"    CheckInterval: {checkInterval.TotalSeconds} seconds");
							Console.WriteLine($"    LaunchDelay: {appConfig.LaunchDelaySeconds} seconds");
							Console.WriteLine($"    CPU Threshold: {appConfig.CpuThresholdPercent}%");
							Console.WriteLine($"    Memory Threshold: {appConfig.MemoryThresholdMB}MB");
							Console.WriteLine($"    Graceful Shutdown Timeout: {appConfig.GracefulShutdownTimeoutSeconds} seconds");
							Console.WriteLine($"    Restart Backoff (Initial/Max Attempts/Max Delay): {appConfig.InitialRestartDelaySeconds}s / {appConfig.MaxRestartAttempts} / {appConfig.MaxRestartDelaySeconds}s");
							Console.WriteLine($"    Circuit Breaker (Failures/Reset Timeout): {appConfig.CircuitBreakerFailureThreshold} / {appConfig.CircuitBreakerResetTimeoutMinutes}min");

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
								Console.ForegroundColor = ConsoleColor.Cyan;
								Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Pausing for {appConfig.LaunchDelaySeconds} seconds before starting the next monitor...");
								Console.ResetColor();
								try
								{
									await Task.Delay(TimeSpan.FromSeconds(appConfig.LaunchDelaySeconds), linkedCts.Token);
								}
								catch (OperationCanceledException)
								{
									Console.ForegroundColor = ConsoleColor.Yellow;
									Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Launch delay cancelled during monitor setup.");
									Console.ResetColor();
									break;
								}
							}
						}

						if (!currentMonitoringTasks.Any())
						{
							Console.ForegroundColor = ConsoleColor.Yellow;
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] No valid applications were launched for monitoring in this cycle.");
							Console.ResetColor();
							// If no valid apps, orchestration loop will naturally hit startMonitoringEvent.Wait() again
							continue;
						}
						else
						{
							Console.ForegroundColor = ConsoleColor.Green;
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] All configured application monitors are now active and running.");
							Console.ResetColor();
						}

						// Wait for all current monitoring tasks to complete
						try
						{
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Waiting for current monitoring tasks to complete or be cancelled...");
							await Task.WhenAll(currentMonitoringTasks);
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] All current monitoring tasks completed normally.");
						}
						catch (OperationCanceledException)
						{
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] One or more monitoring tasks were cancelled (e.g., by 'stop' or daemon shutdown).");
							// Do not re-throw, allow cleanup to proceed.
						}
					} // currentMonitoringCts is disposed here

					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Current monitoring cycle concluded. Initiating cleanup of applications.");
					Console.ResetColor();

					foreach (var monitor in activeMonitors.ToList())
					{
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Calling KillApplication for a monitor.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Applications cleaned up for this cycle.");
					Console.ResetColor();
				}
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Monitoring orchestration loop exited.");
			}
			catch (OperationCanceledException)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] Monitoring orchestration loop was cancelled by daemon shutdown.");
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Orchestration] An unhandled error occurred in the monitoring orchestration loop: {ex.Message}");
				Console.ResetColor();
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
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("\n--- Available Commands ---");
				foreach (var cmd in commands.Values.OrderBy(c => c.Name))
				{
					Console.WriteLine($"  {cmd.Name,-15} - {cmd.Description}");
				}
				Console.WriteLine("--------------------------");
				Console.ResetColor();
				await Task.CompletedTask;
			}));

			commands.Add("start", new ConsoleCommand("start", "Starts monitoring all configured applications.", async () =>
			{
				if (IsMonitoringActive())
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Monitoring is already active.");
					Console.ResetColor();
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Cyan;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'start' command received. Signalling monitoring to begin...");
					Console.ResetColor();
					startEvent.Set();
				}
				await Task.CompletedTask;
			}));

			commands.Add("stop", new ConsoleCommand("stop", "Gracefully terminates monitored applications and returns to waiting state.", async () =>
			{
				if (IsMonitoringActive())
				{
					Console.ForegroundColor = ConsoleColor.Cyan;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'stop' command received. Cancelling current monitoring cycle (currentMonitoringCts)...");
					Console.ResetColor();
					currentMonitoringCts.Cancel();
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Monitoring is not active, or already stopping.");
					Console.ResetColor();
				}
				await Task.CompletedTask;
			}));

			commands.Add("force-kill", new ConsoleCommand("force-kill", "Immediately terminates all monitored applications, bypassing graceful shutdown.", async () =>
			{
				if (IsMonitoringActive())
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'force-kill' command received. Immediately terminating all monitored processes...");
					Console.ResetColor();

					foreach (var monitor in activeMonitors.ToList())
					{
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Force-killing monitor process directly.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					currentMonitoringCts.Cancel(); // Ensure the monitoring loop itself also stops
					startEvent.Reset(); // Reset the start event so it waits for 'start' again
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] No active monitoring to force-kill.");
					Console.ResetColor();
				}
				await Task.CompletedTask;
			}));

			commands.Add("force-restart", new ConsoleCommand("force-restart", "Immediately terminates and then restarts all applications.", async () =>
			{
				if (IsMonitoringActive()) // Use the updated check
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'force-restart' command received. Immediately terminating and then restarting all monitored processes...");
					Console.ResetColor();

					foreach (var monitor in activeMonitors.ToList())
					{
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Force-killing monitor process directly for restart.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					currentMonitoringCts.Cancel(); // Signal current cycle to stop
					startEvent.Set(); // Signal to restart the orchestration loop immediately after cleanup
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Restart sequence initiated. Applications will re-launch shortly.");
					Console.ResetColor();
				}
				else if (!startEvent.IsSet) // Only signal start if not already waiting for it
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Monitoring is not active. Signalling 'start' to launch applications for force-restart.");
					Console.ResetColor();
					startEvent.Set();
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Cannot force-restart: Monitoring cycle is already stopping or in an unexpected state.");
					Console.ResetColor();
				}
				await Task.CompletedTask;
			}));

			commands.Add("shutdown", new ConsoleCommand("shutdown", "Gracefully stops the daemon and all monitored applications.", async () =>
			{
				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] 'shutdown' command received. Initiating graceful daemon shutdown...");
				Console.ResetColor();

				if (currentMonitoringCts != null && !currentMonitoringCts.IsCancellationRequested)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Signalled active monitoring cycle to stop first.");
					currentMonitoringCts.Cancel();
				}
				sharedDaemonCts.Cancel(); // This will eventually stop the main orchestration loop and console reader
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
					continue; // If input is empty, just loop and re-prompt
				}

				if (commands.TryGetValue(input, out var command))
				{
					await command.Action();
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Unknown command: '{input}'. Type 'help' to see available commands.");
					Console.ResetColor();
				}
			}
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Console command reader stopped.");
		}
	}
}