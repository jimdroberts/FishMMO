/*using System;
using UnityEngine;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Monitoring.Health;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;

namespace FishMMO.Server.Database
{
	/// <summary>
	/// Production-ready Unity MonoBehaviour for comprehensive database health monitoring.
	/// Features:
	/// - Automatic health checks with configurable intervals
	/// - Connection pool monitoring with threshold-based alerts
	/// - Query performance tracking and slow query detection
	/// - Metrics collection and reporting
	/// - Inspector-visible status for debugging
	/// - Context menu commands for manual operations
	/// Designed for Unity headless servers with minimal performance overhead.
	/// Thread-safe and compatible with Unity's main thread requirements.
	/// </summary>
	public class DatabaseHealthService : MonoBehaviour
	{
		private FishMMO.Database.Database database;
		private bool isInitialized;
		private bool isMonitoring;

		[Header("Health Check Configuration")]
		[SerializeField]
		[Tooltip("Interval in seconds between automatic health checks")]
		private float healthCheckInterval = 30f;

		[SerializeField]
		[Tooltip("Delay in seconds before first health check")]
		private float initialHealthCheckDelay = 5f;

		[SerializeField]
		[Tooltip("Enable automatic health checks")]
		private bool enableHealthChecks = true;

		[Header("Pool Monitoring Configuration")]
		[SerializeField]
		[Tooltip("Enable connection pool monitoring")]
		private bool enablePoolMonitoring = true;

		[SerializeField]
		[Tooltip("Interval in seconds between pool health checks (lightweight, no DB query)")]
		private float poolCheckInterval = 15f;

		[SerializeField]
		[Tooltip("Pool utilization warning threshold percentage (70-85)")]
		private float poolWarningThreshold = 70f;

		[SerializeField]
		[Tooltip("Pool utilization critical threshold percentage (85-95)")]
		private float poolCriticalThreshold = 85f;

		[Header("Metrics Configuration")]
		[SerializeField]
		[Tooltip("Enable periodic metrics refresh (no logging; server subscribes to events)")]
		private bool enableMetricsRefresh = true;

		[SerializeField]
		[Tooltip("Interval in seconds between metrics refresh")]
		private float metricsRefreshInterval = 60f;

		[Header("Status Display (Read-Only)")]
		[SerializeField]
		[Tooltip("Current database health status")]
		private string currentHealthStatus = "Not Initialized";

		[SerializeField]
		[Tooltip("Last health check message")]
		private string lastHealthMessage = "No checks performed";

		[SerializeField]
		[Tooltip("Response time in milliseconds")]
		private float lastResponseTimeMs = 0f;

		[SerializeField]
		[Tooltip("Connection pool status")]
		private string poolStatus = "Unknown";

		[SerializeField]
		[Tooltip("Pool utilization percentage")]
		private float poolUtilization = 0f;

		[SerializeField]
		[Tooltip("Active connections / Max pool size")]
		private string poolConnections = "0/0";

		[SerializeField]
		[Tooltip("Total queries executed since startup")]
		private long totalQueries = 0;

		[SerializeField]
		[Tooltip("Query success rate percentage")]
		private float successRate = 100f;

		[SerializeField]
		[Tooltip("Average query response time in milliseconds")]
		private float avgResponseTimeMs = 0f;

		// Events for external systems (server-side logging/telemetry happens outside this DLL)
		public event Action<HealthCheckResult> OnHealthCheckCompleted;
		public event Action<HealthCheckResult> OnHealthStatusChanged;
		public event Action<PoolHealthResult> OnPoolHealthChecked;
		public event Action<PoolHealthResult> OnPoolStatusChanged;
		public event Action<SlowQueryEventArgs> OnSlowQueryDetected;
		public event Action<MetricsSummary> OnMetricsSummaryUpdated;
		public event Action<Exception> OnMonitoringError;

		/// <summary>
		/// Gets the last recorded health check result.
		/// </summary>
		public HealthCheckResult LastHealthResult { get; private set; }

		/// <summary>
		/// Gets the last recorded pool health result.
		/// </summary>
		public PoolHealthResult LastPoolHealth { get; private set; }

		/// <summary>
		/// Gets a value indicating whether the database health service is initialized.
		/// </summary>
		public bool IsInitialized => isInitialized;

		/// <summary>
		/// Gets a value indicating whether monitoring is actively running.
		/// </summary>
		public bool IsMonitoring => isMonitoring;

		/// <summary>
		/// Initializes the database health service with the database orchestrator.
		/// Called by the server orchestrator during startup.
		/// </summary>
		/// <param name="database">The database orchestrator instance.</param>
		/// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
		/// <exception cref="FishMMO.Database.Exceptions.DatabaseException">Thrown when already initialized.</exception>
		public void Initialize(FishMMO.Database.Database database)
		{
			if (database == null)
				throw new ArgumentNullException(nameof(database));

			if (isInitialized)
				throw new FishMMO.Database.Exceptions.DatabaseException(
					"DatabaseHealthService is already initialized.",
					"INVALID_OPERATION",
					isTransient: false);

			this.database = database;
			isInitialized = true;

			// Subscribe to slow query events (server decides what to do with them)
			if (database.DbContextFactory is Npgsql.INpgsqlDbContextFactory npgsqlFactory)
			{
				npgsqlFactory.PerformanceTracker.SlowQueryDetected += OnSlowQueryDetectedInternal;
			}

			// Start monitoring
			StartMonitoring();

			// Intentionally no logging here; server project should handle logging.
		}

		/// <summary>
		/// Starts all monitoring tasks.
		/// </summary>
		public void StartMonitoring()
		{
			if (!isInitialized || isMonitoring)
				return;

			// Start periodic health checks
			if (enableHealthChecks)
			{
				InvokeRepeating(nameof(PerformHealthCheck), initialHealthCheckDelay, healthCheckInterval);
			}

			// Start periodic pool monitoring
			if (enablePoolMonitoring)
			{
				InvokeRepeating(nameof(CheckPoolHealth), poolCheckInterval / 2f, poolCheckInterval);
			}

			// Start periodic metrics refresh (no logging)
			if (enableMetricsRefresh)
			{
				InvokeRepeating(nameof(RefreshMetrics), metricsRefreshInterval, metricsRefreshInterval);
			}

			isMonitoring = true;
		}

		/// <summary>
		/// Stops all monitoring tasks.
		/// </summary>
		public void StopMonitoring()
		{
			if (!isMonitoring)
				return;

			CancelInvoke();
			isMonitoring = false;
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
				var previousStatus = LastHealthResult?.Status ?? HealthStatus.Unknown;
				LastHealthResult = database.HealthMonitor.CheckHealth();
				OnHealthCheckCompleted?.Invoke(LastHealthResult);

				// Update inspector-visible fields
				currentHealthStatus = LastHealthResult.Status.ToString();
				lastHealthMessage = LastHealthResult.Message;
				lastResponseTimeMs = (float)LastHealthResult.ResponseTimeMs;

				// Update pool info from health check
				poolStatus = LastHealthResult.PoolHealthStatus.ToString();
				poolUtilization = (float)LastHealthResult.PoolUtilizationPercent;
				poolConnections = $"{LastHealthResult.ActiveConnections}/{LastHealthResult.MaxPoolSize}";

				// Notify subscribers if status changed
				if (LastHealthResult.Status != previousStatus)
				{
					OnHealthStatusChanged?.Invoke(LastHealthResult);
				}
			}
			catch (Exception ex)
			{
				OnMonitoringError?.Invoke(ex);
				currentHealthStatus = "Error";
				lastHealthMessage = ex.Message;
			}
		}

		/// <summary>
		/// Checks connection pool health without performing a database query.
		/// Lightweight operation suitable for frequent monitoring.
		/// </summary>
		private void CheckPoolHealth()
		{
			if (!isInitialized || database == null)
				return;

			try
			{
				var previousStatus = LastPoolHealth?.Status ?? PoolHealthStatus.Healthy;
				LastPoolHealth = database.HealthMonitor.GetPoolHealth(poolWarningThreshold, poolCriticalThreshold);
				OnPoolHealthChecked?.Invoke(LastPoolHealth);

				// Update inspector fields
				poolStatus = LastPoolHealth.Status.ToString();
				poolUtilization = (float)LastPoolHealth.UtilizationPercent;
				poolConnections = $"{LastPoolHealth.ActiveConnections}/{LastPoolHealth.MaxPoolSize}";

				// Notify subscribers if status changed
				if (LastPoolHealth.Status != previousStatus)
				{
					OnPoolStatusChanged?.Invoke(LastPoolHealth);
				}
			}
			catch (Exception ex)
			{
				OnMonitoringError?.Invoke(ex);
			}
		}

		/// <summary>
		/// Refreshes current database metrics and emits them via events.
		/// Called automatically at configured intervals if enableMetricsRefresh is true.
		/// </summary>
		private void RefreshMetrics()
		{
			if (!isInitialized || database == null)
				return;

			try
			{
				// Query performance tracker metrics (per-operation)
				if (database.DbContextFactory is Npgsql.INpgsqlDbContextFactory npgsqlFactory)
				{
					var allMetrics = npgsqlFactory.PerformanceTracker.GetAllMetrics();
					var poolMetrics = npgsqlFactory.PoolMetrics;

					if (allMetrics.Count > 0)
					{
						long totalExecutions = 0;
						long successfulExecutions = 0;
						double totalDurationMs = 0;

						foreach (var metric in allMetrics)
						{
							totalExecutions += metric.TotalExecutions;
							successfulExecutions += metric.SuccessfulExecutions;
							totalDurationMs += metric.AverageMs * metric.TotalExecutions;
						}

						// Update inspector fields
						totalQueries = totalExecutions;
						successRate = totalExecutions > 0 ? (float)(successfulExecutions * 100.0 / totalExecutions) : 100f;
						avgResponseTimeMs = totalExecutions > 0 ? (float)(totalDurationMs / totalExecutions) : 0f;
					}
				}

				// Aggregate metrics tracker snapshot (if your server is recording into it)
				// Note: This DLL does not log. Server should subscribe to OnMetricsSummaryUpdated.
				var summary = database.MetricsTracker?.GetSummary();
				if (summary != null)
				{
					OnMetricsSummaryUpdated?.Invoke(summary);
				}
			}
			catch (Exception ex)
			{
				OnMonitoringError?.Invoke(ex);
			}
		}

		/// <summary>
		/// Internal handler for slow query events from the performance tracker.
		/// </summary>
		private void OnSlowQueryDetectedInternal(object sender, SlowQueryEventArgs e)
		{
			// Notify external subscribers (server logs/alerts if desired)
			OnSlowQueryDetected?.Invoke(e);
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
		/// Manually checks pool health outside of the automatic schedule.
		/// </summary>
		public void ManualPoolCheck()
		{
			CheckPoolHealth();
		}

		/// <summary>
		/// Manually retrieves and logs current metrics outside of the automatic schedule.
		/// </summary>
		public void ManualMetricsRefresh()
		{
			RefreshMetrics();
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
		/// Gets a formatted health report for logging or display.
		/// </summary>
		/// <returns>Multi-line formatted health report.</returns>
		public string GetHealthReport()
		{
			if (!isInitialized || LastHealthResult == null)
				return "Health monitoring not initialized";

			return $"=== Database Health Report ===\n" +
				   $"Status: {LastHealthResult.Status}\n" +
				   $"Message: {LastHealthResult.Message}\n" +
				   $"Response Time: {LastHealthResult.ResponseTimeMs:F2}ms\n" +
				   $"Pool Status: {LastHealthResult.PoolHealthStatus}\n" +
				   $"Pool Utilization: {LastHealthResult.PoolUtilizationPercent:F1}%\n" +
				   $"Active Connections: {LastHealthResult.ActiveConnections}/{LastHealthResult.MaxPoolSize}\n" +
				   $"Pool Exhaustions: {LastHealthResult.PoolExhaustionCount}";
		}

		/// <summary>
		/// Called when the MonoBehaviour is destroyed.
		/// Cancels all pending InvokeRepeating calls and unsubscribes from events.
		/// </summary>
		private void OnDestroy()
		{
			StopMonitoring();

			// Unsubscribe from slow query events
			if (database?.DbContextFactory is Npgsql.INpgsqlDbContextFactory npgsqlFactory)
			{
				npgsqlFactory.PerformanceTracker.SlowQueryDetected -= OnSlowQueryDetectedInternal;
			}

			isInitialized = false;
			// Intentionally no logging here; server project should handle logging.
		}

		#region Context Menu Commands (Unity Editor)

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
		/// Unity Editor command to manually check pool health.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Check Pool Health")]
		private void ContextMenu_CheckPoolHealth()
		{
			ManualPoolCheck();
		}

		/// <summary>
		/// Unity Editor command to manually log metrics.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Log Metrics")]
		private void ContextMenu_LogMetrics()
		{
			ManualMetricsRefresh();
		}

		/// <summary>
		/// Unity Editor command to print full health report.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Print Health Report")]
		private void ContextMenu_PrintHealthReport()
		{
			// Server project: log or display GetHealthReport() result.
			// Example: ServerLogger.Info(GetHealthReport());
		}

		/// <summary>
		/// Unity Editor command to start monitoring.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Start Monitoring")]
		private void ContextMenu_StartMonitoring()
		{
			StartMonitoring();
		}

		/// <summary>
		/// Unity Editor command to stop monitoring.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Stop Monitoring")]
		private void ContextMenu_StopMonitoring()
		{
			StopMonitoring();
		}

		#endregion
	}
}*/