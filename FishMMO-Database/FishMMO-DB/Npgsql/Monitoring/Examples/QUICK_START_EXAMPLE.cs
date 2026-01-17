using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Monitoring.Health;

namespace FishMMO.Database.Examples
{
	/// <summary>
	/// Quick start example for pool health monitoring.
	/// Copy and adapt this code to your server startup.
	/// </summary>
	public static class QuickStartExample
	{
		/// <summary>
		/// Minimal setup for pool health monitoring.
		/// </summary>
		public static async Task MinimalSetup()
		{
			// Initialize database with pool monitoring enabled
			var database = new Database(
				configPath: "./Config",
				enableLogging: false,
				commandTimeout: 10,
				healthCheckWarningMs: 100,
				healthCheckCriticalMs: 500);

			// Perform initial health check
			var health = await database.HealthMonitor.CheckHealthAsync();

			Console.WriteLine($"Database: {health.Status}");
			Console.WriteLine($"Pool: {health.PoolHealthStatus} - {health.PoolHealthMessage}");

			// Check if immediate action needed
			if (health.PoolRequiresAction)
			{
				Console.WriteLine($"WARNING: {health.PoolHealthMessage}");
				// Send alert to monitoring system
			}
		}

		/// <summary>
		/// Background monitoring task for Unity servers.
		/// Run this as a background task in your server startup.
		/// </summary>
		public static async Task StartBackgroundMonitoring(
			Database database,
			CancellationToken cancellationToken)
		{
			var checkInterval = TimeSpan.FromMinutes(1);

			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					// Lightweight pool check (no database query)
					var poolHealth = database.HealthMonitor.GetPoolHealth();

					// Log current status
					LogPoolMetrics(poolHealth);

					// Trigger alerts if needed
					if (poolHealth.Status == PoolHealthStatus.Unhealthy)
					{
						await SendCriticalAlert(poolHealth);
					}
					else if (poolHealth.Status == PoolHealthStatus.Critical)
					{
						await SendWarningAlert(poolHealth);
					}

					await Task.Delay(checkInterval, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Pool monitoring error: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// Health check endpoint for load balancers (HTTP 200 OK / 503 Service Unavailable).
		/// </summary>
		public static async Task<(int statusCode, string message)> HealthCheckEndpoint(
			Database database)
		{
			try
			{
				var health = await database.HealthMonitor.CheckHealthAsync();

				// Return 503 if database or pool is unhealthy
				if (health.Status == HealthStatus.Unhealthy ||
					health.PoolHealthStatus == PoolHealthStatus.Unhealthy)
				{
					return (503, $"Service Unavailable: {health.Message}");
				}

				// Return 200 with health details
				return (200, $"Healthy - DB: {health.ResponseTimeMs:F2}ms, Pool: {health.PoolUtilizationPercent:F1}%");
			}
			catch (Exception ex)
			{
				return (503, $"Health check failed: {ex.Message}");
			}
		}

		private static void LogPoolMetrics(PoolHealthResult poolHealth)
		{
			Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] " +
							  $"Pool Status: {poolHealth.Status} | " +
							  $"Utilization: {poolHealth.UtilizationPercent:F1}% | " +
							  $"Active: {poolHealth.ActiveConnections}/{poolHealth.MaxPoolSize}");
		}

		private static Task SendCriticalAlert(PoolHealthResult poolHealth)
		{
			Console.WriteLine("==========================================");
			Console.WriteLine("CRITICAL ALERT: Pool Unhealthy");
			Console.WriteLine($"Message: {poolHealth.Message}");
			Console.WriteLine($"Action: {poolHealth.RecommendedAction}");
			Console.WriteLine("==========================================");

			// TODO: Integrate with your alerting system
			// await Slack.SendAsync($"🚨 CRITICAL: {poolHealth.Message}");
			// await PagerDuty.TriggerIncident(poolHealth);

			return Task.CompletedTask;
		}

		private static Task SendWarningAlert(PoolHealthResult poolHealth)
		{
			Console.WriteLine($"⚠️  WARNING: {poolHealth.Message}");
			Console.WriteLine($"Recommendation: {poolHealth.RecommendedAction}");

			// TODO: Integrate with your alerting system
			// await Slack.SendAsync($"⚠️  WARNING: {poolHealth.Message}");

			return Task.CompletedTask;
		}
	}
}
