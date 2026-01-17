/*
 * POOL HEALTH MONITORING INTEGRATION EXAMPLE
 * 
 * This example demonstrates how to integrate connection pool health monitoring
 * into your application for proactive alerting and diagnostics.
 * 
 * Features:
 * - Get pool health status without full database connectivity check
 * - Monitor pool utilization with configurable thresholds
 * - Receive actionable recommendations for pool issues
 * - Integrate pool health into overall database health checks
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Monitoring.Health;

namespace FishMMO.Database.Examples
{
	/// <summary>
	/// Example demonstrating pool health monitoring integration.
	/// </summary>
	public static class PoolHealthMonitoringExample
	{
		/// <summary>
		/// Example 1: Basic pool health check.
		/// Get current pool health status without performing database connectivity check.
		/// </summary>
		public static void BasicPoolHealthCheck()
		{
			var database = new Database(enableLogging: false);

			// Get current pool health
			var poolHealth = database.HealthMonitor.GetPoolHealth();

			Console.WriteLine($"Pool Health Status: {poolHealth.Status}");
			Console.WriteLine($"Message: {poolHealth.Message}");
			Console.WriteLine($"Utilization: {poolHealth.UtilizationPercent:F1}%");
			Console.WriteLine($"Active Connections: {poolHealth.ActiveConnections}/{poolHealth.MaxPoolSize}");
			Console.WriteLine($"Recommended Action: {poolHealth.RecommendedAction}");

			if (poolHealth.RequiresAction)
			{
				Console.WriteLine("WARNING: Pool health requires immediate attention!");
			}
		}

		/// <summary>
		/// Example 2: Full health check with pool monitoring.
		/// Perform comprehensive database health check including pool health.
		/// </summary>
		public static async Task FullHealthCheckWithPoolMonitoring()
		{
			var database = new Database(
				enableLogging: false,
				healthCheckWarningMs: 100,
				healthCheckCriticalMs: 500);

			// Perform full health check
			var healthResult = await database.HealthMonitor.CheckHealthAsync();

			Console.WriteLine($"Database Status: {healthResult.Status}");
			Console.WriteLine($"Response Time: {healthResult.ResponseTimeMs:F2}ms");
			Console.WriteLine($"Pool Health: {healthResult.PoolHealthStatus}");
			Console.WriteLine($"Pool Message: {healthResult.PoolHealthMessage}");

			if (healthResult.PoolRequiresAction)
			{
				Console.WriteLine("ALERT: Pool health degraded!");
				Console.WriteLine($"Active Connections: {healthResult.ActiveConnections}");
				Console.WriteLine($"Pool Utilization: {healthResult.PoolUtilizationPercent:F1}%");
				Console.WriteLine($"Exhaustion Count: {healthResult.PoolExhaustionCount}");
			}
		}

		/// <summary>
		/// Example 3: Periodic pool health monitoring with alerting.
		/// Monitor pool health continuously and trigger alerts on threshold violations.
		/// </summary>
		public static async Task PeriodicPoolMonitoring(CancellationToken cancellationToken)
		{
			var database = new Database(enableLogging: false);
			var checkInterval = TimeSpan.FromSeconds(30);

			Console.WriteLine("Starting periodic pool health monitoring...");

			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					var poolHealth = database.HealthMonitor.GetPoolHealth();

					// Log metrics
					Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Pool Status: {poolHealth.Status} | " +
									  $"Utilization: {poolHealth.UtilizationPercent:F1}%");

					// Trigger alerts based on status
					switch (poolHealth.Status)
					{
						case PoolHealthStatus.Unhealthy:
							await TriggerCriticalAlert(poolHealth);
							break;

						case PoolHealthStatus.Critical:
							await TriggerWarningAlert(poolHealth);
							break;

						case PoolHealthStatus.Warning:
							LogWarning(poolHealth);
							break;
					}

					await Task.Delay(checkInterval, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error during pool monitoring: {ex.Message}");
				}
			}

			Console.WriteLine("Pool health monitoring stopped.");
		}

		/// <summary>
		/// Example 4: Custom thresholds for pool health.
		/// Configure custom warning and critical thresholds for specific workloads.
		/// </summary>
		public static void CustomThresholdExample()
		{
			var database = new Database(enableLogging: false);

			// Get pool metrics directly from factory
			if (database.DbContextFactory is Npgsql.NpgsqlDbContextFactory factory)
			{
				var metrics = factory.PoolMetrics;

				// Custom thresholds for high-load scenarios
				var poolHealth = metrics.GetPoolHealth(
					maxPoolSize: factory.MaxPoolSize,
					warningThreshold: 60.0,   // Lower threshold for early warning
					criticalThreshold: 75.0); // Lower critical threshold

				Console.WriteLine($"Custom Threshold Check: {poolHealth.Status}");
				Console.WriteLine($"Utilization: {poolHealth.UtilizationPercent:F1}%");
				Console.WriteLine($"Recommendation: {poolHealth.RecommendedAction}");
			}
		}

		/// <summary>
		/// Example 5: Integration with monitoring systems.
		/// Export pool health metrics to external monitoring systems (Prometheus, Grafana, etc.).
		/// </summary>
		public static void ExportMetricsForMonitoring()
		{
			var database = new Database(enableLogging: false);
			var poolHealth = database.HealthMonitor.GetPoolHealth();

			// Export metrics in a format suitable for monitoring systems
			var metrics = new
			{
				pool_health_status = (int)poolHealth.Status,
				pool_utilization_percent = poolHealth.UtilizationPercent,
				pool_active_connections = poolHealth.ActiveConnections,
				pool_max_size = poolHealth.MaxPoolSize,
				pool_peak_connections = poolHealth.PeakActiveConnections,
				pool_exhaustion_count = poolHealth.PoolExhaustionCount,
				pool_connection_errors = poolHealth.ConnectionErrors,
				pool_requires_action = poolHealth.RequiresAction ? 1 : 0
			};

			// Example: Send to monitoring endpoint (pseudo-code)
			// await SendToPrometheus(metrics);
			// await SendToDatadog(metrics);

			Console.WriteLine("Metrics ready for export:");
			Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(metrics, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		}

		/// <summary>
		/// Example 6: Health check endpoint for load balancers.
		/// Implement health endpoint that fails when pool is unhealthy.
		/// </summary>
		public static async Task<bool> HealthCheckEndpoint()
		{
			var database = new Database(enableLogging: false);
			var healthResult = await database.HealthMonitor.CheckHealthAsync();

			// Return healthy only if both database and pool are healthy
			bool isHealthy = healthResult.Status == HealthStatus.Healthy &&
							 healthResult.PoolHealthStatus != PoolHealthStatus.Unhealthy;

			Console.WriteLine($"Health Endpoint: {(isHealthy ? "PASS" : "FAIL")}");
			Console.WriteLine($"Database: {healthResult.Status}");
			Console.WriteLine($"Pool: {healthResult.PoolHealthStatus}");

			return isHealthy;
		}

		#region Helper Methods

		private static Task TriggerCriticalAlert(PoolHealthResult poolHealth)
		{
			Console.WriteLine("===============================================");
			Console.WriteLine("CRITICAL ALERT: Pool Health Unhealthy");
			Console.WriteLine($"Message: {poolHealth.Message}");
			Console.WriteLine($"Utilization: {poolHealth.UtilizationPercent:F1}%");
			Console.WriteLine($"Action Required: {poolHealth.RecommendedAction}");
			Console.WriteLine("===============================================");

			// Example: Send to alerting system
			// await SlackNotification.SendAsync($"CRITICAL: {poolHealth.Message}");
			// await PagerDutyAlert.TriggerAsync(poolHealth);

			return Task.CompletedTask;
		}

		private static Task TriggerWarningAlert(PoolHealthResult poolHealth)
		{
			Console.WriteLine($"WARNING: Pool health critical - {poolHealth.Message}");
			Console.WriteLine($"Recommendation: {poolHealth.RecommendedAction}");

			// Example: Send to alerting system
			// await SlackNotification.SendAsync($"WARNING: {poolHealth.Message}");

			return Task.CompletedTask;
		}

		private static void LogWarning(PoolHealthResult poolHealth)
		{
			Console.WriteLine($"INFO: Pool utilization elevated - {poolHealth.Message}");
		}

		#endregion

		/// <summary>
		/// Example usage in Unity server startup.
		/// </summary>
		public static async Task UnityServerIntegrationExample()
		{
			Console.WriteLine("Initializing FishMMO Database with Pool Monitoring...");

			var database = new Database(
				configPath: "./Config",
				enableLogging: false,
				commandTimeout: 10,
				healthCheckWarningMs: 100,
				healthCheckCriticalMs: 500);

			// Initial health check
			var initialHealth = await database.HealthMonitor.CheckHealthAsync();
			Console.WriteLine($"Initial Database Health: {initialHealth.Status}");
			Console.WriteLine($"Initial Pool Health: {initialHealth.PoolHealthStatus}");

			if (initialHealth.PoolRequiresAction)
			{
				Console.WriteLine($"WARNING: {initialHealth.PoolHealthMessage}");
			}

			// Start background monitoring (example)
			var cts = new CancellationTokenSource();
			var monitoringTask = Task.Run(async () =>
			{
				while (!cts.Token.IsCancellationRequested)
				{
					try
					{
						var poolHealth = database.HealthMonitor.GetPoolHealth();

						if (poolHealth.Status >= PoolHealthStatus.Critical)
						{
							Console.WriteLine($"[MONITOR] Pool Alert: {poolHealth.Message}");
						}

						await Task.Delay(TimeSpan.FromMinutes(1), cts.Token);
					}
					catch (OperationCanceledException)
					{
						break;
					}
				}
			}, cts.Token);

			Console.WriteLine("Pool monitoring active. Press any key to stop...");
			// In Unity: Monitor continuously until server shutdown
		}
	}
}
