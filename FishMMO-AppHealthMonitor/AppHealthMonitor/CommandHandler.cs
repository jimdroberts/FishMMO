using System.Text;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Handles console command input and execution for the daemon.
	/// Provides commands for starting, stopping, restarting, querying status, and shutting down monitored applications.
	/// </summary>
	public sealed class CommandHandler
	{
		/// <summary>
		/// The daemon orchestrator that owns all monitoring state.
		/// </summary>
		private readonly DaemonOrchestrator orchestrator;

		/// <summary>
		/// Whether the daemon is running in headless mode (no interactive console prompt).
		/// </summary>
		private readonly bool headless;

		/// <summary>
		/// Registered console commands keyed by case-insensitive command name.
		/// </summary>
		private readonly Dictionary<string, ConsoleCommand> commands = new Dictionary<string, ConsoleCommand>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Pre-built help text string, cached after all commands are registered.
		/// </summary>
		private string cachedHelpText = string.Empty;

		/// <summary>
		/// Initializes a new instance of the <see cref="CommandHandler"/> class.
		/// </summary>
		/// <param name="orchestrator">The daemon orchestrator that owns all monitoring state.</param>
		/// <param name="headless">Whether the daemon is running in headless mode (suppresses console prompt).</param>
		public CommandHandler(DaemonOrchestrator orchestrator, bool headless)
		{
			ArgumentNullException.ThrowIfNull(orchestrator);

			this.orchestrator = orchestrator;
			this.headless = headless;

			RegisterCommands();
			BuildHelpText();
		}

		/// <summary>
		/// Builds and caches the sorted help text string. Called once after all commands are registered.
		/// </summary>
		private void BuildHelpText()
		{
			var sortedCommands = new List<ConsoleCommand>(commands.Values);
			sortedCommands.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

			var builder = new StringBuilder();
			builder.AppendLine("--- Available Commands ---");
			foreach (var cmd in sortedCommands)
			{
				builder.AppendLine($"  {cmd.Name,-15} - {cmd.Description}");
			}
			builder.Append("--------------------------");
			cachedHelpText = builder.ToString();
		}

		/// <summary>
		/// Registers a console command, eliminating name duplication.
		/// </summary>
		/// <param name="name">The keyword used to invoke the command.</param>
		/// <param name="description">A brief description of what the command does.</param>
		/// <param name="action">The asynchronous action to perform when the command is invoked.</param>
		private void Register(string name, string description, Func<Task> action)
		{
			commands.Add(name, new ConsoleCommand(name, description, action));
		}

		/// <summary>
		/// Force-kills all active monitored processes and awaits cycle cleanup completion.
		/// Shared by force-kill and force-restart commands to avoid duplicated logic.
		/// </summary>
		/// <returns>A task that completes when all processes are terminated and the cycle has cleaned up.</returns>
		private async Task ForceKillAndWaitAsync()
		{
			var cycleCompletion = await orchestrator.ForceKillAllAsync();
			if (cycleCompletion != null)
			{
				Log.Info("DaemonCommand", "Waiting for monitoring cycle cleanup to complete...");
				await cycleCompletion;
			}
		}

		/// <summary>
		/// Registers all available console commands.
		/// </summary>
		private void RegisterCommands()
		{
			Register("help", "Lists all available commands.", () =>
			{
				Log.Info("DaemonCommand", cachedHelpText);
				return Task.CompletedTask;
			});

			Register("start", "Starts monitoring all configured applications.", () =>
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
			});

			Register("stop", "Gracefully terminates monitored applications and returns to waiting state.", () =>
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
			});

			Register("force-kill", "Immediately terminates all monitored applications, bypassing graceful shutdown.", async () =>
			{
				if (orchestrator.IsMonitoringActive())
				{
					Log.Error("DaemonCommand", "'force-kill' command received. Immediately terminating all monitored processes...");
					await ForceKillAndWaitAsync();
					Log.Info("DaemonCommand", "All processes terminated and cycle cleanup complete.");
				}
				else
				{
					Log.Warning("DaemonCommand", "No active monitoring to force-kill.");
				}
			});

			Register("force-restart", "Immediately terminates and then restarts all applications.", async () =>
			{
				if (orchestrator.IsMonitoringActive())
				{
					Log.Error("DaemonCommand", "'force-restart' command received. Immediately terminating and restarting all monitored processes...");
					await ForceKillAndWaitAsync();
					orchestrator.TrySignalStart();
					Log.Info("DaemonCommand", "Restart sequence initiated. Applications will re-launch shortly.");
				}
				else
				{
					Log.Warning("DaemonCommand", "Monitoring is not active. Signalling 'start' to launch applications.");
					orchestrator.TrySignalStart();
				}
			});

			Register("status", "Displays the current status of all monitored applications.", () =>
			{
				string monitoringState = orchestrator.IsMonitoringActive() ? "ACTIVE" : "WAITING";
				var statuses = orchestrator.GetActiveMonitorStatuses();

				Log.Info("DaemonCommand", $"--- Monitor Status (Monitoring: {monitoringState}) ---");
				if (statuses.Count == 0)
				{
					Log.Info("DaemonCommand", "  No active monitors.");
				}
				else
				{
					foreach (var status in statuses)
					{
						string pid = status.ProcessId.HasValue ? status.ProcessId.Value.ToString() : "N/A";
						string state = status.MaxRestartsReached ? "EXHAUSTED"

							: !status.HasCompletedInitialCheck ? "STARTING"
							: status.IsRunning ? "HEALTHY"
							: "DOWN";
						Log.Info("DaemonCommand",
							$"  {status.Name,-20} PID: {pid,-8} State: {state,-14} Restarts: {status.RestartAttempts}/{status.MaxRestartAttempts} PortFail: {status.ConsecutivePortFailures} ResFail: {status.ConsecutiveResourceFailures}");
					}
				}
				Log.Info("DaemonCommand", "----------------------");
				return Task.CompletedTask;
			});

			Func<Task> shutdownAction = () =>
			{
				Log.Info("DaemonCommand", "Shutdown requested. Initiating graceful daemon shutdown...");
				orchestrator.Shutdown();
				return Task.CompletedTask;
			};
			Register("shutdown", "Gracefully stops the daemon and all monitored applications.", shutdownAction);
			Register("exit", "Alias for 'shutdown'.", shutdownAction);
		}

		/// <summary>
		/// Runs the console command reader loop using cancellable <see cref="TextReader.ReadLineAsync(CancellationToken)"/>.
		/// Dispatches commands until the daemon is shut down.
		/// </summary>
		/// <returns>A task representing the asynchronous command reading operation.</returns>
		public async Task RunAsync()
		{
			while (!orchestrator.IsDaemonShutdownRequested)
			{
				if (!headless)
				{
					Console.Write("Daemon Command > ");
				}

				string? input;
				try
				{
					input = await Console.In.ReadLineAsync(orchestrator.DaemonShutdownToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				// null means EOF (stdin closed/piped). Break to avoid an infinite busy-loop.
				if (input == null)
				{
					Log.Info("DaemonCommand", "EOF detected on stdin. Command reader stopping.");
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

				input = input.Trim();

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