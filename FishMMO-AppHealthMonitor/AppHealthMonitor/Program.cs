﻿using Microsoft.Extensions.Configuration;
using FishMMO.Logging;

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

		private static readonly string loggingConfigName = "logging.json";

		// Represents a console command
		private class ConsoleCommand
		{
			public string Name { get; }
			public string Description { get; }
			public Func<Task> Action { get; }

			public ConsoleCommand(string name, string description, Func<Task> action)
			{
				Name = name;
				Description = description;
				Action = action;
			}
		}

		static async Task Main(string[] args)
		{
			string workingDirectory = Directory.GetCurrentDirectory();

			// Load Application Configuration from appsettings.json
			var builder = new ConfigurationBuilder()
				.SetBasePath(workingDirectory)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

			IConfiguration configuration = builder.Build();

			// Load Logging Configuration from logging.json
			string configFilePath = Path.Combine(workingDirectory, loggingConfigName);

			// Initialize the Log manager.
			Log.Initialize(configFilePath, new ConsoleFormatter(), null, Log.OnInternalLogMessage);

			Log.Info("Daemon", "Starting Application Health Monitor Daemon...");

			var appConfigs = configuration.GetSection("Applications").Get<List<AppConfig>>();

			if (appConfigs == null || appConfigs.Count == 0)
			{
				Log.Critical("Daemon", "Error: No application configurations found in 'Applications' section of appsettings.json. Please configure at least one application.");
				await Task.Delay(1000);
				await Log.Shutdown();
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
						Log.Info("Daemon", "\nCtrl+C pressed. Signalling daemon shutdown...");
						sharedDaemonCts.Cancel();
						eventArgs.Cancel = true; // Prevent immediate termination
					}
				};

				// The main orchestration loop that manages start/stop of application monitors
				Task orchestrationLoopTask = RunMonitoringOrchestrationLoop(appConfigs, sharedDaemonCts.Token);

				// Start the console command reader in parallel
				Task consoleReaderTask = ConsoleCommandReader(sharedDaemonCts, startMonitoringEvent);

				Log.Info("Daemon", "\nApplication Health Monitor Daemon is ready.");
				Log.Info("Daemon", "Type 'help' to list available commands.");

				// Wait for the main orchestration loop and console reader to complete (when daemon-wide shutdown is requested)
				await Task.WhenAll(orchestrationLoopTask, consoleReaderTask);

				// Final cleanup: ensure all processes are terminated when the daemon itself is stopping.
				Log.Warning("Daemon", $"All daemon tasks have concluded. Initiating final cleanup of all monitored applications.");

				foreach (var monitor in activeMonitors.ToList())
				{
					monitor.KillApplication();
				}
				activeMonitors.Clear();

				Log.Warning("Daemon", $"All monitored applications terminated. Application Health Monitor Daemon stopped gracefully.");
			}
			await Log.Shutdown();
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
					Log.Info("Orchestration", "Waiting for 'start' command...");

					try
					{
						await Task.Run(() => startMonitoringEvent.Wait(daemonCancellationToken), daemonCancellationToken);
					}
					catch (OperationCanceledException)
					{
						Log.Info("Orchestration", "Waiting for start command cancelled. Daemon shutting down.");
						break;
					}

					startMonitoringEvent.Reset();

					Log.Info("Orchestration", "'start' command received. Launching application monitors.");

					using (currentMonitoringCts = new CancellationTokenSource())
					using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(currentMonitoringCts.Token, daemonCancellationToken))
					{
						activeMonitors.Clear();
						List<Task> currentMonitoringTasks = new List<Task>();

						for (int i = 0; i < appConfigs.Count; i++)
						{
							if (linkedCts.Token.IsCancellationRequested)
							{
								Log.Warning("Orchestration", "Monitoring launch cancelled during setup.");
								break;
							}

							var appConfig = appConfigs[i];

							if (string.IsNullOrWhiteSpace(appConfig.Name) ||
								string.IsNullOrWhiteSpace(appConfig.ApplicationExePath))
							{
								Log.Warning("Orchestration", $"Skipping invalid configuration entry during launch: Name='{appConfig.Name}' ExePath='{appConfig.ApplicationExePath}'.");
								continue;
							}

							if (appConfig.PortTypes == null || !appConfig.PortTypes.Any())
							{
								appConfig.PortTypes = new List<PortType> { PortType.None };
							}

							TimeSpan checkInterval = TimeSpan.FromSeconds(appConfig.CheckIntervalSeconds > 0 ? appConfig.CheckIntervalSeconds : 10);

							// Log a header for the application launch
							Log.Info("Orchestration", $"--- Launching Monitor for: [{appConfig.Name}] ---");

							// Create a dictionary to hold application configuration data for logging
							var appDetails = new Dictionary<string, object>
							{
								{ "ApplicationExePath", appConfig.ApplicationExePath },
								{ "MonitoredPort", appConfig.MonitoredPort },
								{ "PortTypes", string.Join(", ", appConfig.PortTypes) }, // Convert list to string for logging
                                { "LaunchArguments", appConfig.LaunchArguments },
								{ "CheckInterval", $"{checkInterval.TotalSeconds}s" },
								{ "LaunchDelay", $"{appConfig.LaunchDelaySeconds}s" },
								{ "CpuThreshold", $"{appConfig.CpuThresholdPercent}%" },
								{ "MemoryThreshold", $"{appConfig.MemoryThresholdMB}MB" },
								{ "GracefulShutdownTimeout", $"{appConfig.GracefulShutdownTimeoutSeconds}s" },
								{ "InitialRestartDelay", $"{appConfig.InitialRestartDelaySeconds}s" },
								{ "MaxRestartDelay", $"{appConfig.MaxRestartDelaySeconds}s" },
								{ "MaxRestartAttempts", appConfig.MaxRestartAttempts },
								{ "CircuitBreakerFailureThreshold", appConfig.CircuitBreakerFailureThreshold },
								{ "CircuitBreakerResetTimeout", $"{appConfig.CircuitBreakerResetTimeoutMinutes}min" }
							};

							// Log the structured application details
							Log.Info("Orchestration", $"Application Configuration for {appConfig.Name}:", data: appDetails);


							HealthMonitor monitor = new HealthMonitor(
								appConfig.Name,
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

						if (!currentMonitoringTasks.Any())
						{
							Log.Warning("Orchestration", "No valid applications were launched for monitoring in this cycle.");
							continue;
						}
						else
						{
							Log.Info("Orchestration", "All configured application monitors are now active and running.");
						}

						try
						{
							Log.Debug("Orchestration", "Waiting for current monitoring tasks to complete or be cancelled...");
							await Task.WhenAll(currentMonitoringTasks);
							Log.Debug("Orchestration", "All current monitoring tasks completed normally.");
						}
						catch (OperationCanceledException)
						{
							Log.Info("Orchestration", "One or more monitoring tasks were cancelled (e.g., by 'stop' or daemon shutdown).");
						}
					} // currentMonitoringCts is disposed here

					Log.Warning("Orchestration", "Current monitoring cycle concluded. Initiating cleanup of applications.");

					foreach (var monitor in activeMonitors.ToList())
					{
						Log.Debug("Orchestration", "Calling KillApplication for a monitor.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					Log.Warning("Orchestration", "Applications cleaned up for this cycle.");
				}
				Log.Info("Orchestration", "Monitoring orchestration loop exited.");
			}
			catch (OperationCanceledException ex)
			{
				Log.Info("Orchestration", $"Monitoring orchestration loop was cancelled by daemon shutdown.", ex);
			}
			catch (Exception ex)
			{
				Log.Critical("Orchestration", $"An unhandled error occurred in the monitoring orchestration loop: {ex.Message}", ex);
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
				Log.Info("DaemonCommand", "--- Available Commands ---\n");
				foreach (var cmd in commands.Values.OrderBy(c => c.Name))
				{
					Log.Info("DaemonCommand", $"  {cmd.Name,-15} - {cmd.Description}");
				}
				Log.Info("DaemonCommand", "--------------------------");
				await Task.CompletedTask;
			}));

			commands.Add("start", new ConsoleCommand("start", "Starts monitoring all configured applications.", async () =>
			{
				if (IsMonitoringActive())
				{
					Log.Warning("DaemonCommand", "Monitoring is already active.");
				}
				else
				{
					Log.Info("DaemonCommand", "'start' command received. Signalling monitoring to begin...");
					startEvent.Set();
				}
				await Task.CompletedTask;
			}));

			commands.Add("stop", new ConsoleCommand("stop", "Gracefully terminates monitored applications and returns to waiting state.", async () =>
			{
				if (IsMonitoringActive())
				{
					Log.Info("DaemonCommand", "'stop' command received. Cancelling current monitoring cycle (currentMonitoringCts)...");
					currentMonitoringCts.Cancel();
				}
				else
				{
					Log.Warning("DaemonCommand", "Monitoring is not active, or already stopping.");
				}
				await Task.CompletedTask;
			}));

			commands.Add("force-kill", new ConsoleCommand("force-kill", "Immediately terminates all monitored applications, bypassing graceful shutdown.", async () =>
			{
				if (IsMonitoringActive())
				{
					Log.Error("DaemonCommand", "'force-kill' command received. Immediately terminating all monitored processes...");

					foreach (var monitor in activeMonitors.ToList())
					{
						Log.Debug("DaemonCommand", "Force-killing monitor process directly.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					currentMonitoringCts.Cancel();
					startEvent.Reset();
				}
				else
				{
					Log.Warning("DaemonCommand", "No active monitoring to force-kill.");
				}
				await Task.CompletedTask;
			}));

			commands.Add("force-restart", new ConsoleCommand("force-restart", "Immediately terminates and then restarts all applications.", async () =>
			{
				if (IsMonitoringActive())
				{
					Log.Error("DaemonCommand", "'force-restart' command received. Immediately terminating and then restarting all monitored processes...");

					foreach (var monitor in activeMonitors.ToList())
					{
						Log.Debug("DaemonCommand", "Force-killing monitor process directly for restart.");
						monitor.KillApplication();
					}
					activeMonitors.Clear();

					currentMonitoringCts.Cancel();
					startEvent.Set();
					Log.Info("DaemonCommand", "Restart sequence initiated. Applications will re-launch shortly.");
				}
				else if (!startEvent.IsSet)
				{
					Log.Warning("DaemonCommand", "Monitoring is not active. Signalling 'start' to launch applications for force-restart.");
					startEvent.Set();
				}
				else
				{
					Log.Warning("DaemonCommand", "Cannot force-restart: Monitoring cycle is already stopping or in an unexpected state.");
				}
				await Task.CompletedTask;
			}));

			commands.Add("shutdown", new ConsoleCommand("shutdown", "Gracefully stops the daemon and all monitored applications.", async () =>
			{
				Log.Info("DaemonCommand", "'shutdown' command received. Initiating graceful daemon shutdown...");

				if (currentMonitoringCts != null && !currentMonitoringCts.IsCancellationRequested)
				{
					Log.Debug("DaemonCommand", "Signalled active monitoring cycle to stop first.");
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
					Log.Warning("DaemonCommand", $"Unknown command: '{input}'. Type 'help' to see available commands.");
				}
			}
			Log.Info("DaemonCommand", "Console command reader stopped.");
		}
	}
}