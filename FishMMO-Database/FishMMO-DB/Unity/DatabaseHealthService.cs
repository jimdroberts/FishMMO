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
		[Tooltip("Enable automatic metrics logging to Unity console")]
		private bool enableMetricsLogging = true;

		[SerializeField]
		[Tooltip("Interval in seconds between metrics logging")]
		private float metricsLogInterval = 60f;

		[Header("Alerting Configuration")]
		[SerializeField]
		[Tooltip("Enable console alerts for critical health issues")]
		private bool enableAlerts = true;

		[SerializeField]
		[Tooltip("Enable slow query logging")]
		private bool enableSlowQueryLogging = true;

		[SerializeField]
		[Tooltip("Slow query threshold in milliseconds")]
		private float slowQueryThresholdMs = 1000f;

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

		// Event for external systems to subscribe to health changes
		public event Action<HealthCheckResult> OnHealthStatusChanged;
		public event Action<PoolHealthResult> OnPoolStatusChanged;
		public event Action<SlowQueryEventArgs> OnSlowQueryDetected;

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
		/// <exception cref="InvalidOperationException">Thrown when already initialized.</exception>
		public void Initialize(FishMMO.Database.Database database)
		{
			if (database == null)
				throw new ArgumentNullException(nameof(database));

			if (isInitialized)
				throw new InvalidOperationException("DatabaseHealthService is already initialized.");

			this.database = database;
			isInitialized = true;

			// Subscribe to slow query events if enabled
			if (enableSlowQueryLogging && database.DbContextFactory is Npgsql.INpgsqlDbContextFactory npgsqlFactory)
			{
				npgsqlFactory.PerformanceTracker.SlowQueryDetected += OnSlowQueryDetectedInternal;
			}

			// Start monitoring
			StartMonitoring();

			Debug.Log("[DatabaseHealthService] Initialized successfully.");
		}

		/// <summary>
		/// Starts all monitoring tasks.
		/// </summary>
		public void StartMonitoring()
		{
			if (!isInitialized)
			{
				Debug.LogWarning("[DatabaseHealthService] Cannot start monitoring - not initialized.");
				return;
			}

			if (isMonitoring)
			{
				Debug.LogWarning("[DatabaseHealthService] Monitoring is already running.");
				return;
			}

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

			// Start periodic metrics logging
			if (enableMetricsLogging)
			{
				InvokeRepeating(nameof(LogMetrics), metricsLogInterval, metricsLogInterval);
			}

			isMonitoring = true;
			Debug.Log("[DatabaseHealthService] Monitoring started.");
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
			Debug.Log("[DatabaseHealthService] Monitoring stopped.");
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

				// Log based on severity
				if (enableAlerts)
				{
					if (LastHealthResult.Status == HealthStatus.Unhealthy)
					{
						Debug.LogError($"[DatabaseHealthService] 🔴 UNHEALTHY: {LastHealthResult.Message}");
					}
					else if (LastHealthResult.Status == HealthStatus.Degraded)
					{
						Debug.LogWarning($"[DatabaseHealthService] 🟡 DEGRADED: {LastHealthResult.Message}");
					}
					else if (LastHealthResult.PoolRequiresAction)
					{
						Debug.LogWarning($"[DatabaseHealthService] ⚠️  POOL WARNING: {LastHealthResult.PoolHealthMessage}");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[DatabaseHealthService] Health check failed: {ex.Message}");
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
				LastPoolHealth = database.HealthMonitor.GetPoolHealth();

				// Update inspector fields
				poolStatus = LastPoolHealth.Status.ToString();
				poolUtilization = (float)LastPoolHealth.UtilizationPercent;
				poolConnections = $"{LastPoolHealth.ActiveConnections}/{LastPoolHealth.MaxPoolSize}";

				// Notify subscribers if status changed
				if (LastPoolHealth.Status != previousStatus)
				{
					OnPoolStatusChanged?.Invoke(LastPoolHealth);
				}

				// Alert on critical pool conditions
				if (enableAlerts)
				{
					if (LastPoolHealth.Status == PoolHealthStatus.Unhealthy)
					{
						Debug.LogError($"[DatabaseHealthService] 🚨 POOL CRITICAL: {LastPoolHealth.Message}\n" +
									   $"Action Required: {LastPoolHealth.RecommendedAction}");
					}
					else if (LastPoolHealth.Status == PoolHealthStatus.Critical)
					{
						Debug.LogWarning($"[DatabaseHealthService] ⚠️  POOL CRITICAL: {LastPoolHealth.Message}\n" +
										$"Recommendation: {LastPoolHealth.RecommendedAction}");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[DatabaseHealthService] Pool health check failed: {ex.Message}");
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
				// Get query performance metrics if available
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

						Debug.Log($"[DatabaseHealthService] 📊 Metrics Summary:\n" +
								  $"  Total Queries: {totalExecutions:N0}\n" +
								  $"  Success Rate: {successRate:F2}%\n" +
								  $"  Avg Response: {avgResponseTimeMs:F2}ms\n" +
								  $"  Pool: {poolMetrics.ActiveConnections} active / {poolMetrics.TotalConnectionsCreated} created / {poolMetrics.ConnectionErrors} errors");

						// Log top 5 slowest operations
						var slowest = npgsqlFactory.PerformanceTracker.GetSlowestOperations(5);
						if (slowest.Count > 0)
						{
							Debug.Log("[DatabaseHealthService] 🐌 Slowest Operations:");
							foreach (var op in slowest)
							{
								Debug.Log($"  {op.OperationName}: {op.AverageMs:F2}ms avg ({op.TotalExecutions} calls)");
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[DatabaseHealthService] Failed to retrieve metrics: {ex.Message}");
			}
		}

		/// <summary>
		/// Internal handler for slow query events from the performance tracker.
		/// </summary>
		private void OnSlowQueryDetectedInternal(object sender, SlowQueryEventArgs e)
		{
			if (enableSlowQueryLogging)
			{
				Debug.LogWarning($"[DatabaseHealthService] 🐢 SLOW QUERY: {e.OperationName} took {e.Duration.TotalMilliseconds:F2}ms " +
								$"(threshold: {slowQueryThresholdMs}ms) - Success: {e.Success}");
			}

			// Notify external subscribers
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
		public void ManualMetricsLog()
		{
			LogMetrics();
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
			Debug.Log("[DatabaseHealthService] Shutdown complete.");
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
			ManualMetricsLog();
		}

		/// <summary>
		/// Unity Editor command to print full health report.
		/// Available in the component context menu.
		/// </summary>
		[ContextMenu("Print Health Report")]
		private void ContextMenu_PrintHealthReport()
		{
			Debug.Log(GetHealthReport());
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