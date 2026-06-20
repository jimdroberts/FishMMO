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
		/// Registers POSIX signal handlers for both SIGTERM and SIGINT on supported Unix
		/// platforms. Both are treated identically — graceful daemon shutdown.
		/// systemd uses SIGTERM by default (KillSignal=) but operators may send SIGINT
		/// (Ctrl+C via systemctl, or when KillSignal= is overridden). Handling both
		/// ensures clean shutdown regardless of which signal arrives.
		/// Returns null on unsupported platforms so daemon startup is never blocked.
		/// </summary>
		/// <param name="orchestrator">The orchestrator to signal for daemon shutdown.</param>
		/// <returns>A list of signal registrations when supported; otherwise, null.</returns>
		private static List<PosixSignalRegistration>? TryRegisterPosixSignalHandlers(DaemonOrchestrator orchestrator)
		{
			if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
			{
				return null;
			}

			var registrations = new List<PosixSignalRegistration>();
			try
			{
				Action<PosixSignalContext> handler = context =>
				{
					if (!orchestrator.IsDaemonShutdownRequested)
					{
						Log.Info("Daemon", $"{context.Signal} received. Signalling daemon shutdown...");
						orchestrator.Shutdown();
					}
					context.Cancel = true;
				};

				registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, handler));
				registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, handler));
				return registrations;
			}
			catch (PlatformNotSupportedException)
			{
				foreach (var reg in registrations)
				{
					reg.Dispose();
				}
				Log.Warning("Daemon", "POSIX signal handlers are not supported on this platform/runtime. Continuing without signal interception.");
				return null;
			}
		}

		/// <summary>
		/// Resolves a configuration file path by preferring the working directory first
		/// (operator override), then falling back to the bundled application directory.
		/// </summary>
		/// <param name="fileName">The file name to resolve (e.g., "appsettings.json").</param>
		/// <param name="bundledDirectory">The directory containing the bundled default.</param>
		/// <returns>The resolved absolute file path.</returns>
		private static string ResolveConfigPath(string fileName, string bundledDirectory)
		{
			string localPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
			if (File.Exists(localPath))
			{
				return localPath;
			}
			return Path.Combine(bundledDirectory, fileName);
		}

		/// <summary>
		/// Main entry point for the Application Health Monitor daemon.
		/// Initializes configuration, logging, and starts the orchestration loop.
		/// </summary>
		/// <returns>A task representing the asynchronous operation.</returns>
		static async Task Main()
		{
			string applicationBaseDirectory = AppContext.BaseDirectory;

			// Resolve config path: working-directory override first, then bundled default.
			string appSettingsPath = ResolveConfigPath("appsettings.json", applicationBaseDirectory);
			IConfigurationRoot configuration;
			try
			{
				configuration = new ConfigurationBuilder()
					.AddJsonFile(appSettingsPath, optional: false, reloadOnChange: false)
					.AddEnvironmentVariables()
					.Build();
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to load appsettings.json: {ex.Message}");
				Console.Error.WriteLine($"Ensure appsettings.json exists in '{Path.GetDirectoryName(appSettingsPath)}' and contains valid JSON.");
				Environment.ExitCode = 1;
				return;
			}

			string configFilePath = ResolveConfigPath(LoggingConfigName, applicationBaseDirectory);
			try
			{
				Log.Initialize(configFilePath, new ConsoleFormatter());
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to initialize logging from '{LoggingConfigName}': {ex.Message}");
				Console.Error.WriteLine($"Ensure {LoggingConfigName} exists in '{applicationBaseDirectory}' and contains valid JSON.");
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

					// POSIX signal interception (SIGTERM + SIGINT) registered only on supported Unix platforms.
					// On those platforms, Cancel=true suppresses Environment.Exit so await using can unwind.
					// Both signals trigger the same graceful shutdown path — systemd uses SIGTERM by
					// default, but SIGINT can arrive via systemctl or when KillSignal= is overridden.
					var sigRegistrations = TryRegisterPosixSignalHandlers(orchestrator);
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
						if (sigRegistrations != null)
						{
							foreach (var reg in sigRegistrations)
							{
								reg.Dispose();
							}
						}
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