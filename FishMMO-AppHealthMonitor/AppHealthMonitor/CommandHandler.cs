using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Handles console command input and execution for the daemon.
	/// Provides commands for starting, stopping, restarting, querying status, and shutting down monitored applications.
	/// </summary>
	public sealed class CommandHandler
	{
		private readonly DaemonOrchestrator orchestrator;
		private readonly Dictionary<string, ConsoleCommand> commands = new Dictionary<string, ConsoleCommand>();
		private string cachedHelpText = string.Empty;

		/// <summary>
		/// Initializes a new instance of the <see cref="CommandHandler"/> class.
		/// </summary>
		/// <param name="orchestrator">The daemon orchestrator that owns all monitoring state.</param>
		public CommandHandler(DaemonOrchestrator orchestrator)
		{
			this.orchestrator = orchestrator;

			RegisterCommands();
			BuildHelpText();
		}

		/// <summary>
		/// Builds and caches the sorted help text string. Called once after all commands are registered.
		/// </summary>
		private void BuildHelpText()
		{
			var sortedNames = new List<string>(commands.Keys);
			sortedNames.Sort(StringComparer.Ordinal);

			var builder = new System.Text.StringBuilder();
			builder.AppendLine("--- Available Commands ---");
			foreach (var name in sortedNames)
			{
				var cmd = commands[name];
				builder.AppendLine($"  {cmd.Name,-15} - {cmd.Description}");
			}
			builder.Append("--------------------------");
			cachedHelpText = builder.ToString();
		}

		/// <summary>
		/// Registers all available console commands.
		/// </summary>
		private void RegisterCommands()
		{
			commands.Add("help", new ConsoleCommand("help", "Lists all available commands.", () =>
			{
				Log.Info("DaemonCommand", cachedHelpText);
				return Task.CompletedTask;
			}));

			commands.Add("start", new ConsoleCommand("start", "Starts monitoring all configured applications.", () =>
			{
				if (orchestrator.IsMonitoringActive())
				{
					Log.Warning("DaemonCommand", "Monitoring is already active.");
				}
				else
				{
					Log.Info("DaemonCommand", "'start' command received. Signalling monitoring to begin...");
					orchestrator.TrySignalStart();
				}
				return Task.CompletedTask;
			}));

			commands.Add("stop", new ConsoleCommand("stop", "Gracefully terminates monitored applications and returns to waiting state.", () =>
			{
				if (orchestrator.IsMonitoringActive())
				{
					Log.Info("DaemonCommand", "'stop' command received. Cancelling current monitoring cycle...");
					orchestrator.CancelCurrentMonitoring();
				}
				else
				{
					Log.Warning("DaemonCommand", "Monitoring is not active, or already stopping.");
				}
				return Task.CompletedTask;
			}));

			commands.Add("force-kill", new ConsoleCommand("force-kill", "Immediately terminates all monitored applications, bypassing graceful shutdown.", async () =>
			{
				if (orchestrator.IsMonitoringActive())
				{
					Log.Error("DaemonCommand", "'force-kill' command received. Immediately terminating all monitored processes...");
					await orchestrator.ForceKillAllAsync();
				}
				else
				{
					Log.Warning("DaemonCommand", "No active monitoring to force-kill.");
				}
			}));

			commands.Add("force-restart", new ConsoleCommand("force-restart", "Immediately terminates and then restarts all applications.", async () =>
			{
				if (orchestrator.IsMonitoringActive())
				{
					Log.Error("DaemonCommand", "'force-restart' command received. Immediately terminating and restarting all monitored processes...");
					await orchestrator.ForceKillAllAsync();
					Log.Info("DaemonCommand", "Waiting for monitoring cycle cleanup to complete...");
					await orchestrator.AwaitCycleCompletionAsync();
					orchestrator.TrySignalStart();
					Log.Info("DaemonCommand", "Restart sequence initiated. Applications will re-launch shortly.");
				}
				else
				{
					Log.Warning("DaemonCommand", "Monitoring is not active. Signalling 'start' to launch applications.");
					orchestrator.TrySignalStart();
				}
			}));

			commands.Add("status", new ConsoleCommand("status", "Displays the current status of all monitored applications.", () =>
			{
				if (!orchestrator.IsMonitoringActive())
				{
					Log.Info("DaemonCommand", "Monitoring is not active.");
					return Task.CompletedTask;
				}

				var statuses = orchestrator.GetActiveMonitorStatuses();
				if (statuses.Count == 0)
				{
					Log.Info("DaemonCommand", "No active monitors.");
					return Task.CompletedTask;
				}

				Log.Info("DaemonCommand", "--- Monitor Status ---");
				foreach (var status in statuses)
				{
					string pid = status.ProcessId.HasValue ? status.ProcessId.Value.ToString() : "N/A";
					string state = status.MaxRestartsReached ? "EXHAUSTED"
						: status.IsCircuitOpen ? "CIRCUIT OPEN"
						: status.IsRunning ? "HEALTHY"
						: "DOWN";
					Log.Info("DaemonCommand",
						$"  {status.Name,-20} PID: {pid,-8} State: {state,-14} Restarts: {status.RestartAttempts}/{status.MaxRestartAttempts}");
				}
				Log.Info("DaemonCommand", "----------------------");
				return Task.CompletedTask;
			}));

			commands.Add("shutdown", new ConsoleCommand("shutdown", "Gracefully stops the daemon and all monitored applications.", () =>
			{
				Log.Info("DaemonCommand", "'shutdown' command received. Initiating graceful daemon shutdown...");
				orchestrator.Shutdown();
				return Task.CompletedTask;
			}));

			commands.Add("exit", new ConsoleCommand("exit", "Alias for 'shutdown'.", () =>
			{
				Log.Info("DaemonCommand", "'exit' command received. Initiating graceful daemon shutdown...");
				orchestrator.Shutdown();
				return Task.CompletedTask;
			}));
		}

		/// <summary>
		/// Runs the console command reader loop using <see cref="TextReader.ReadLineAsync()"/>
		/// for cancellable async reads. Dispatches commands until the daemon is shut down.
		/// </summary>
		/// <returns>A task representing the asynchronous command reading operation.</returns>
		public async Task RunAsync()
		{
			while (!orchestrator.IsDaemonShutdownRequested)
			{
				Console.Write("Daemon Command > ");

				string? input;
				try
				{
					input = await Console.In.ReadLineAsync();
				}
				catch (OperationCanceledException)
				{
					break;
				}

				if (orchestrator.IsDaemonShutdownRequested)
				{
					break;
				}

				if (string.IsNullOrWhiteSpace(input))
				{
					continue;
				}

				input = input.ToLowerInvariant();

				if (commands.TryGetValue(input, out var command))
				{
					try
					{
						await command.Action();
					}
					catch (Exception ex)
					{
						Log.Error("DaemonCommand", $"Error executing command '{input}': {ex.Message}", ex);
					}
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