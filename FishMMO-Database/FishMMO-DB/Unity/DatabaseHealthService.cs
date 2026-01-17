/*using System;
using UnityEngine;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Monitoring.Health;
using FishMMO.Database.Npgsql.Monitoring.Metrics;

namespace FishMMO.Server.Database
{
	/// <summary>
	/// Unity MonoBehaviour for monitoring database health and metrics.
	/// Integrates with the Database orchestrator for periodic health checks and metrics reporting.
	/// Designed to be initialized by the server orchestrator with dependency injection.
	/// Follows Single Responsibility Principle: solely responsible for Unity integration of database monitoring.
	/// </summary>
	public class DatabaseHealthService : MonoBehaviour
	{
		private FishMMO.Database.Database database;
		private bool isInitialized;

		[Header("Health Check Configuration")]
		[SerializeField]
		[Tooltip("Interval in seconds between automatic health checks")]
		private float healthCheckInterval = 30f;

		[SerializeField]
		[Tooltip("Delay in seconds before first health check")]
		private float initialHealthCheckDelay = 5f;

		[Header("Metrics Configuration")]
		[SerializeField]
		[Tooltip("Enable automatic metrics logging to Unity console")]
		private bool enableMetricsLogging = true;

		[SerializeField]
		[Tooltip("Interval in seconds between metrics logging")]
		private float metricsLogInterval = 60f;

		[Header("Status Display")]
		[SerializeField]
		[Tooltip("Current health status (read-only)")]
		private string currentHealthStatus = "Not Initialized";

		[SerializeField]
		[Tooltip("Last health check message (read-only)")]
		private string lastHealthMessage = "No checks performed";

		[SerializeField]
		[Tooltip("Response time in milliseconds (read-only)")]
		private float lastResponseTimeMs = 0f;

		/// <summary>
		/// Gets the last recorded health check result.
		/// </summary>
		public HealthCheckResult LastHealthResult { get; private set; }

		/// <summary>
		/// Gets the last recorded metrics summary.
		/// </summary>
		public MetricsSummary LastMetrics { get; private set; }

		/// <summary>
		/// Gets a value indicating whether the database health service is initialized.
		/// </summary>
		public bool IsInitialized => isInitialized;

		/// <summary>
		/// Initializes the database health service with the database orchestrator.
		/// Called by the server orchestrator during startup.
		/// </summary>
		/// <param name="database">The database orchestrator instance.</param>
		/// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
		/// <exception cref="InvalidOperationException">Thrown when already initialized.</exception>
		public void Initialize(FishMMO.Database.Database database)
		{
			if (database == null)
				throw new ArgumentNullException(nameof(database));

			if (isInitialized)
				throw new InvalidOperationException("DatabaseHealthService is already initialized.");

			this.database = database;
			isInitialized = true;

			// Start periodic health checks
			InvokeRepeating(nameof(PerformHealthCheck), initialHealthCheckDelay, healthCheckInterval);

			// Start periodic metrics logging if enabled
			if (enableMetricsLogging)
			{
				InvokeRepeating(nameof(LogMetrics), metricsLogInterval, metricsLogInterval);
			}

			Debug.Log("[DatabaseHealthService] Initialized and started periodic health monitoring.");
		}

		/// <summary>
		/// Performs a synchronous health check on the database.
		/// Called automatically at configured intervals via InvokeRepeating.
		/// </summary>
		private void PerformHealthCheck()
		{
			if (!isInitialized || database == null)
			{
				Debug.LogWarning("[DatabaseHealthService] Cannot perform health check - not initialized.");
				return;
			}

			try
			{
				// Perform synchronous health check (safe for Unity main thread)
				LastHealthResult = database.HealthMonitor.CheckHealth();

				// Update inspector-visible fields
				currentHealthStatus = LastHealthResult.Status.ToString();
				lastHealthMessage = LastHealthResult.Message;
				lastResponseTimeMs = (float)LastHealthResult.ResponseTimeMs;

				// Log status changes or unhealthy states
				if (LastHealthResult.Status == HealthStatus.Unhealthy)
				{
					Debug.LogError($"[DatabaseHealthService] {LastHealthResult}");
				}
				else if (LastHealthResult.Status == HealthStatus.Degraded)
				{
					Debug.LogWarning($"[DatabaseHealthService] {LastHealthResult}");
				}
				else if (LastHealthResult.HasWarning)
				{
					Debug.LogWarning($"[DatabaseHealthService] {LastHealthResult}");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[DatabaseHealthService] Health check failed with exception: {ex.Message}");
				currentHealthStatus = "Error";
				lastHealthMessage = ex.Message;
			}
		}

		/// <summary>
		/// Logs current database metrics to Unity console.
		/// Called automatically at configured intervals if enableMetricsLogging is true.
		/// </summary>
		private void LogMetrics()
		{
			if (!isInitialized || database == null)
				return;

			try
			{
				LastMetrics = database.MetricsTracker.GetSummary();

				Debug.Log($"[DatabaseHealthService] Database Metrics:\n" +
						  $"  Total Queries: {LastMetrics.TotalQueries}\n" +
						  $"  Success Rate: {LastMetrics.SuccessRate:F2}%\n" +
						  $"  Avg Response: {LastMetrics.AverageResponseTimeMs:F2}ms\n" +
						  $"  Min/Max: {LastMetrics.MinResponseTimeMs:F2}ms / {LastMetrics.MaxResponseTimeMs:F2}ms\n" +
						  $"  Failed Queries: {LastMetrics.FailedQueries}");

				if (LastMetrics.ErrorCounts.Count > 0)
				{
					Debug.Log($"[DatabaseHealthService] Error Breakdown:");
					foreach (var error in LastMetrics.ErrorCounts)
					{
						Debug.Log($"  {error.Key}: {error.Value}");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[DatabaseHealthService] Failed to retrieve metrics: {ex.Message}");
			}
		}

		/// <summary>
		/// Manually triggers a health check outside of the automatic schedule.
		/// Useful for testing or on-demand status verification.
		/// </summary>
		public void ManualHealthCheck()
		{
			PerformHealthCheck();
		}

		/// <summary>
		/// Manually retrieves and logs current metrics outside of the automatic schedule.
		/// </summary>
		public void ManualMetricsLog()
		{
			LogMetrics();
		}

		/// <summary>
		/// Resets all tracked metrics to zero.
		/// Useful for starting fresh metric collection after configuration changes.
		/// </summary>
		public void ResetMetrics()
		{
			if (!isInitialized || database == null)
			{
				Debug.LogWarning("[DatabaseHealthService] Cannot reset metrics - not initialized.");
				return;
			}

			database.MetricsTracker.Reset();
			Debug.Log("[DatabaseHealthService] Database metrics reset.");
		}

		/// <summary>
		/// Gets the current health status without performing a new check.
		/// Returns cached status from the last check.
		/// </summary>
		/// <returns>The last known health status.</returns>
		public HealthStatus GetCurrentHealthStatus()
		{
			if (!isInitialized || database == null)
				return HealthStatus.Unknown;

			return database.HealthMonitor.LastStatus;
		}

		/// <summary>
		/// Gets the current metrics summary without logging.
		/// </summary>
		/// <returns>Current metrics summary, or null if not initialized.</returns>
		public MetricsSummary GetCurrentMetrics()
		{
			if (!isInitialized || database == null)
				return null;

			return database.MetricsTracker.GetSummary();
		}

		/// <summary>
		/// Called when the MonoBehaviour is destroyed.
		/// Cancels all pending InvokeRepeating calls.
		/// </summary>
		private void OnDestroy()
		{
			CancelInvoke();
			isInitialized = false;
			Debug.Log("[DatabaseHealthService] Shutdown and cancelled all health monitoring.");
		}

		/// <summary>
		/// Unity Editor command to manually trigger a health check.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Perform Health Check")]
		private void ContextMenu_PerformHealthCheck()
		{
			ManualHealthCheck();
		}

		/// <summary>
		/// Unity Editor command to manually log metrics.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Log Metrics")]
		private void ContextMenu_LogMetrics()
		{
			ManualMetricsLog();
		}

		/// <summary>
		/// Unity Editor command to reset metrics.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Reset Metrics")]
		private void ContextMenu_ResetMetrics()
		{
			ResetMetrics();
		}
	}
}*/