using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Entry point for the Application Health Monitor daemon.
	/// Responsible only for configuration loading, logging initialization,
	/// and delegating orchestration to <see cref="DaemonOrchestrator"/>.
	/// </summary>
	internal static class Program
	{
		/// <summary>
		/// Name of the logging configuration file.
		/// </summary>
		private const string LoggingConfigName = "logging.json";

		/// <summary>
		/// Main entry point for the Application Health Monitor daemon.
		/// Initializes configuration, logging, and starts the orchestration loop.
		/// </summary>
		/// <returns>A task representing the asynchronous operation.</returns>
		static async Task Main()
		{
			string workingDirectory = Directory.GetCurrentDirectory();

			IConfigurationRoot configuration;
			try
			{
				var builder = new ConfigurationBuilder()
					.SetBasePath(workingDirectory)
					.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

				configuration = builder.Build();
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to load appsettings.json: {ex.Message}");
				Console.Error.WriteLine("Ensure appsettings.json exists in the working directory and contains valid JSON.");
				Environment.ExitCode = 1;
				return;
			}

			string configFilePath = Path.Combine(workingDirectory, LoggingConfigName);
			try
			{
				Log.Initialize(configFilePath, new ConsoleFormatter());
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to initialize logging from '{LoggingConfigName}': {ex.Message}");
				Console.Error.WriteLine("Ensure logging.json exists in the working directory and contains valid JSON.");
				Environment.ExitCode = 1;
				return;
			}

			Log.Info("Daemon", "Starting Application Health Monitor Daemon...");

			List<AppConfig>? appConfigs;
			bool headless;
			try
			{
				appConfigs = configuration.GetSection("Applications").Get<List<AppConfig>>();
				headless = configuration.GetValue<bool>("Headless");
			}
			catch (Exception ex)
			{
				Log.Critical("Daemon", $"Failed to deserialize configuration from appsettings.json: {ex.Message}", ex);
				Environment.ExitCode = 1;
				await Log.Shutdown();
				return;
			}

			if (appConfigs == null || appConfigs.Count == 0)
			{
				Log.Critical("Daemon", "Error: No application configurations found in 'Applications' section of appsettings.json. Please configure at least one application.");
				Environment.ExitCode = 1;
				await Log.Shutdown();
				return;
			}

			DaemonOrchestrator orchestrator;
			try
			{
				orchestrator = new DaemonOrchestrator(appConfigs, headless);
			}
			catch (InvalidOperationException ex)
			{
				Log.Critical("Daemon", ex.Message);
				Environment.ExitCode = 1;
				await Log.Shutdown();
				return;
			}

			try
			{
				await using (orchestrator)
				{
					ConsoleCancelEventHandler cancelHandler = (sender, eventArgs) =>
					{
						if (!orchestrator.IsDaemonShutdownRequested)
						{
							Log.Info("Daemon", "\nCtrl+C pressed. Signalling daemon shutdown...");
							orchestrator.Shutdown();
						}
						// Always suppress default termination to ensure DisposeAsync runs
						// and child processes are cleaned up, matching SIGTERM behavior.
						eventArgs.Cancel = true;
					};
					Console.CancelKeyPress += cancelHandler;

					// SIGTERM (.NET default: Environment.Exit) does NOT await IAsyncDisposable,
					// so DisposeAsync would never run and child processes would be orphaned.
					// Setting Cancel = true suppresses Environment.Exit, allowing the await using
					// block to unwind naturally through the orchestrator's shutdown path.
					using var sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
					{
						if (!orchestrator.IsDaemonShutdownRequested)
						{
							Log.Info("Daemon", "SIGTERM received. Signalling daemon shutdown...");
							orchestrator.Shutdown();
						}
						context.Cancel = true;
					});

					try
					{
						var commandHandler = new CommandHandler(orchestrator, orchestrator.Headless);

						Log.Info("Daemon", "\nApplication Health Monitor Daemon is ready.");

						if (orchestrator.Headless)
						{
							Log.Info("Daemon", "Headless mode active. Auto-starting monitoring.");
							orchestrator.TrySignalStart();
						}
						else
						{
							Log.Info("Daemon", "Type 'help' to list available commands.");
						}

						var orchestratorTask = orchestrator.RunAsync();
						var commandTask = commandHandler.RunAsync();

						try
						{
							await Task.WhenAll(orchestratorTask, commandTask);

							if (orchestrator.HeadlessCycleCompleted)
							{
								Log.Warning("Daemon", "Headless monitoring cycle ended (all monitors exhausted). Exiting with failure code.");
								Environment.ExitCode = 1;
							}
							else
							{
								Log.Info("Daemon", "All daemon tasks have concluded. Application Health Monitor Daemon stopped gracefully.");
							}
						}
						catch (Exception)
						{
							Environment.ExitCode = 1;

							// Task.WhenAll only throws the first exception. Inspect both tasks individually.
							if (orchestratorTask.IsFaulted && orchestratorTask.Exception != null)
							{
								foreach (var ex in orchestratorTask.Exception.InnerExceptions)
								{
									Log.Critical("Daemon", $"Unhandled exception in orchestrator: {ex.Message}", ex);
								}
							}
							if (commandTask.IsFaulted && commandTask.Exception != null)
							{
								foreach (var ex in commandTask.Exception.InnerExceptions)
								{
									Log.Critical("Daemon", $"Unhandled exception in command handler: {ex.Message}", ex);
								}
							}
						}
					}
					finally
					{
						Console.CancelKeyPress -= cancelHandler;
					}
				}
			}
			finally
			{
				await Log.Shutdown();
			}
		}
	}
}