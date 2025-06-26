using Microsoft.Extensions.Configuration;

namespace AppHealthMonitor
{
	class Program
	{
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
				Console.WriteLine("Error: No application configurations found in 'Applications' section of appsettings.json. Please configure at least one application.");
				return;
			}

			Console.WriteLine("Configurations loaded for multiple applications:");
			List<Task> monitoringTasks = new List<Task>();
			// Central CancellationTokenSource to stop all monitors gracefully
			using (var sharedCts = new CancellationTokenSource())
			{
				// Register a single handler for Ctrl+C to cancel the shared token source
				Console.CancelKeyPress += (sender, eventArgs) =>
				{
					if (!sharedCts.IsCancellationRequested)
					{
						Console.WriteLine("\nCtrl+C pressed. Signalling all monitors to stop...");
						sharedCts.Cancel();
						eventArgs.Cancel = true; // Prevent immediate termination
					}
				};

				for (int i = 0; i < appConfigs.Count; i++) // Use a for loop to check index
				{
					var appConfig = appConfigs[i];

					// Basic validation for each application's configuration
					if (string.IsNullOrWhiteSpace(appConfig.Name))
					{
						Console.WriteLine("Warning: An application configuration is missing 'Name'. Skipping this entry.");
						continue;
					}
					if (string.IsNullOrWhiteSpace(appConfig.ApplicationExePath))
					{
						Console.WriteLine($"Error: '{appConfig.Name}' - 'ApplicationExePath' is not configured. Skipping this entry.");
						continue;
					}
					if (string.IsNullOrWhiteSpace(appConfig.ApplicationProcessName))
					{
						Console.WriteLine($"Error: '{appConfig.Name}' - 'ApplicationProcessName' is not configured. Skipping this entry.");
						continue;
					}

					// Ensure PortTypes list is initialized to avoid NullReferenceException
					if (appConfig.PortTypes == null)
					{
						appConfig.PortTypes = new List<PortType> { PortType.None };
					}
					// If PortTypes contains anything other than None, or if it's empty, and MonitoredPort is 0,
					// we might need to adjust default behavior or warn. For now, we'll let HealthMonitor handle.
					// If PortTypes is empty, default to None.
					if (!appConfig.PortTypes.Any())
					{
						appConfig.PortTypes.Add(PortType.None);
					}

					TimeSpan checkInterval = TimeSpan.FromSeconds(appConfig.CheckIntervalSeconds > 0 ? appConfig.CheckIntervalSeconds : 10); // Default to 10 seconds

					Console.WriteLine($"  [{appConfig.Name}]");
					Console.WriteLine($"    ApplicationExePath: {appConfig.ApplicationExePath}");
					Console.WriteLine($"    ApplicationProcessName: {appConfig.ApplicationProcessName}");
					Console.WriteLine($"    MonitoredPort: {appConfig.MonitoredPort} (Types: {string.Join(", ", appConfig.PortTypes)})"); // Show all types
					Console.WriteLine($"    LaunchArguments: {appConfig.LaunchArguments}");
					Console.WriteLine($"    CheckInterval: {checkInterval.TotalSeconds} seconds");
					Console.WriteLine($"    LaunchDelay: {appConfig.LaunchDelaySeconds} seconds"); // New: Show launch delay

					// Create a HealthMonitor instance for each application, passing the shared CancellationToken
					HealthMonitor monitor = new HealthMonitor(
						appConfig.Name,
						appConfig.ApplicationExePath,
						appConfig.ApplicationProcessName,
						appConfig.MonitoredPort,
						appConfig.PortTypes,
						appConfig.LaunchArguments,
						checkInterval,
						sharedCts.Token
					);

					// Add the monitoring task to a list to run concurrently
					monitoringTasks.Add(monitor.StartMonitoring());

					// Add a delay IF it's not the last application
					if (appConfig.LaunchDelaySeconds > 0 && i < appConfigs.Count - 1)
					{
						Console.WriteLine($"[{DateTime.Now}] Pausing for {appConfig.LaunchDelaySeconds} seconds before starting the next monitor...");
						try
						{
							await Task.Delay(TimeSpan.FromSeconds(appConfig.LaunchDelaySeconds), sharedCts.Token);
						}
						catch (OperationCanceledException)
						{
							Console.WriteLine($"[{DateTime.Now}] Launch delay cancelled.");
							break; // Exit the loop if cancelled during delay
						}
					}
				}

				if (monitoringTasks.Count == 0)
				{
					Console.WriteLine("No valid application configurations were found to monitor. Exiting.");
					return;
				}

				Console.WriteLine("\nAll configured applications are now being monitored concurrently.");
				Console.WriteLine("Press Ctrl+C to stop the daemon and all monitoring tasks.");

				// Wait for all monitoring tasks to complete (which will happen when Ctrl+C is pressed and token cancelled)
				await Task.WhenAll(monitoringTasks);

				Console.WriteLine("Application Health Monitor Daemon stopped.");
			}
		}
	}
}