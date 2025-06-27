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
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.WriteLine("Warning: An application configuration is missing 'Name'. Skipping this entry.");
						Console.ResetColor();
						continue;
					}
					if (string.IsNullOrWhiteSpace(appConfig.ApplicationExePath))
					{
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.WriteLine($"Error: '{appConfig.Name}' - 'ApplicationExePath' is not configured. Skipping this entry.");
						Console.ResetColor();
						continue;
					}

					// Ensure PortTypes list is initialized to avoid NullReferenceException
					if (appConfig.PortTypes == null || !appConfig.PortTypes.Any())
					{
						appConfig.PortTypes = new List<PortType> { PortType.None }; // Default to process-only if not specified
					}

					TimeSpan checkInterval = TimeSpan.FromSeconds(appConfig.CheckIntervalSeconds > 0 ? appConfig.CheckIntervalSeconds : 10); // Default to 10 seconds

					Console.WriteLine($"\n--- Application Configuration: [{appConfig.Name}] ---");
					Console.WriteLine($"    ApplicationExePath: {appConfig.ApplicationExePath}");

					// Display monitoring mode based on PortTypes configuration
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

					// Create a HealthMonitor instance for each application, passing the shared CancellationToken
					HealthMonitor monitor = new HealthMonitor(
						appConfig.Name,
						appConfig.ApplicationExePath,
						appConfig.MonitoredPort,
						appConfig.PortTypes,
						appConfig.LaunchArguments,
						checkInterval,
						appConfig.CpuThresholdPercent, // Pass new CPU threshold
						appConfig.MemoryThresholdMB,   // Pass new Memory threshold
						appConfig.GracefulShutdownTimeoutSeconds, // Pass graceful shutdown timeout
						appConfig.InitialRestartDelaySeconds,     // Pass initial restart delay for backoff
						appConfig.MaxRestartDelaySeconds,         // Pass max restart delay for backoff
						appConfig.MaxRestartAttempts,             // Pass max restart attempts for backoff
						appConfig.CircuitBreakerFailureThreshold, // Pass circuit breaker failure threshold
						appConfig.CircuitBreakerResetTimeoutMinutes, // Pass circuit breaker reset timeout
						sharedCts.Token
					);

					// Add the monitoring task to a list to run concurrently
					monitoringTasks.Add(monitor.StartMonitoring());

					// Add a delay IF it's not the last application
					if (appConfig.LaunchDelaySeconds > 0 && i < appConfigs.Count - 1)
					{
						Console.ForegroundColor = ConsoleColor.Cyan;
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Pausing for {appConfig.LaunchDelaySeconds} seconds before starting the next monitor...");
						Console.ResetColor();
						try
						{
							await Task.Delay(TimeSpan.FromSeconds(appConfig.LaunchDelaySeconds), sharedCts.Token);
						}
						catch (OperationCanceledException)
						{
							Console.ForegroundColor = ConsoleColor.Yellow;
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Daemon] Launch delay cancelled due to Ctrl+C.");
							Console.ResetColor();
							break; // Exit the loop if cancelled during delay
						}
					}
				}

				if (monitoringTasks.Count == 0)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine("No valid application configurations were found to monitor. Exiting.");
					Console.ResetColor();
					return;
				}

				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine("\nAll configured applications are now being monitored concurrently.");
				Console.WriteLine("Press Ctrl+C to stop the daemon and all monitoring tasks.");
				Console.ResetColor();

				// Wait for all monitoring tasks to complete (which will happen when Ctrl+C is pressed and token cancelled)
				await Task.WhenAll(monitoringTasks);

				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("Application Health Monitor Daemon stopped gracefully.");
				Console.ResetColor();
			}
		}
	}
}