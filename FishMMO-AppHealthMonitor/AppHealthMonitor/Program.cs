using Microsoft.Extensions.Configuration;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Entry point for the Application Health Monitor daemon.
	/// Responsible only for configuration loading, logging initialization,
	/// and delegating orchestration to <see cref="DaemonOrchestrator"/>.
	/// </summary>
	class Program
	{
		/// <summary>
		/// Name of the logging configuration file.
		/// </summary>
		private const string LoggingConfigName = "logging.json";

		/// <summary>
		/// Main entry point for the Application Health Monitor daemon.
		/// Initializes configuration, logging, and starts the orchestration loop.
		/// </summary>
		/// <param name="args">Command-line arguments (currently unused).</param>
		/// <returns>A task representing the asynchronous operation.</returns>
		static async Task Main(string[] args)
		{
			string workingDirectory = Directory.GetCurrentDirectory();

			var builder = new ConfigurationBuilder()
				.SetBasePath(workingDirectory)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

			IConfiguration configuration = builder.Build();

			string configFilePath = Path.Combine(workingDirectory, LoggingConfigName);
			Log.Initialize(configFilePath, new ConsoleFormatter());

			Log.Info("Daemon", "Starting Application Health Monitor Daemon...");

			var appConfigs = configuration.GetSection("Applications").Get<List<AppConfig>>();

			if (appConfigs == null || appConfigs.Count == 0)
			{
				Log.Critical("Daemon", "Error: No application configurations found in 'Applications' section of appsettings.json. Please configure at least one application.");
				await Task.Delay(1000);
				await Log.Shutdown();
				return;
			}

			DaemonOrchestrator orchestrator;
			try
			{
				orchestrator = new DaemonOrchestrator(appConfigs);
			}
			catch (InvalidOperationException ex)
			{
				Log.Critical("Daemon", ex.Message);
				await Task.Delay(1000);
				await Log.Shutdown();
				return;
			}

			try
			{
				await using (orchestrator)
				{
					Console.CancelKeyPress += (sender, eventArgs) =>
					{
						if (!orchestrator.IsDaemonShutdownRequested)
						{
							Log.Info("Daemon", "\nCtrl+C pressed. Signalling daemon shutdown...");
							orchestrator.Shutdown();
							eventArgs.Cancel = true;
						}
					};

					var commandHandler = new CommandHandler(orchestrator);

					Log.Info("Daemon", "\nApplication Health Monitor Daemon is ready.");
					Log.Info("Daemon", "Type 'help' to list available commands.");

					try
					{
						await Task.WhenAll(orchestrator.RunAsync(), commandHandler.RunAsync());
					}
					catch (Exception ex)
					{
						Log.Critical("Daemon", $"Unhandled exception in daemon tasks: {ex.Message}", ex);
					}

					Log.Warning("Daemon", "All daemon tasks have concluded. Application Health Monitor Daemon stopped gracefully.");
				}
			}
			finally
			{
				await Log.Shutdown();
			}
		}
	}
}