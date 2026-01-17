using System;
using System.Threading;
using FishMMO.Database.Npgsql.Monitoring.Health;

namespace FishMMO.Database.Npgsql.Monitoring.Metrics
{
	/// <summary>
	/// Tracks runtime connection pool metrics for monitoring and diagnostics.
	/// Thread-safe implementation using atomic operations.
	/// </summary>
	public sealed class ConnectionPoolMetrics
	{
		private long totalConnectionsCreated;
		private long totalConnectionsDisposed;
		private long activeConnections;
		private long peakActiveConnections;
		private long connectionErrors;
		private long poolExhaustionCount;
		private readonly object lockObject = new object();

		/// <summary>
		/// Gets the total number of connections created since startup.
		/// </summary>
		public long TotalConnectionsCreated => Interlocked.Read(ref totalConnectionsCreated);

		/// <summary>
		/// Gets the total number of connections disposed since startup.
		/// </summary>
		public long TotalConnectionsDisposed => Interlocked.Read(ref totalConnectionsDisposed);

		/// <summary>
		/// Gets the current number of active connections.
		/// </summary>
		public long ActiveConnections => Interlocked.Read(ref activeConnections);

		/// <summary>
		/// Gets the peak number of concurrent active connections.
		/// </summary>
		public long PeakActiveConnections => Interlocked.Read(ref peakActiveConnections);

		/// <summary>
		/// Gets the total number of connection errors encountered.
		/// </summary>
		public long ConnectionErrors => Interlocked.Read(ref connectionErrors);

		/// <summary>
		/// Gets the number of times the pool was exhausted (all connections in use).
		/// </summary>
		public long PoolExhaustionCount => Interlocked.Read(ref poolExhaustionCount);

		/// <summary>
		/// Gets the approximate pool utilization percentage (0-100).
		/// Returns -1 if max pool size is unknown.
		/// </summary>
		/// <param name="maxPoolSize">The configured maximum pool size.</param>
		/// <returns>Pool utilization percentage or -1 if cannot calculate.</returns>
		public double GetUtilizationPercentage(int maxPoolSize)
		{
			if (maxPoolSize <= 0)
				return -1;

			var active = Interlocked.Read(ref activeConnections);
			return (double)active / maxPoolSize * 100.0;
		}

		/// <summary>
		/// Records a new connection being created.
		/// Thread-safe operation.
		/// </summary>
		public void RecordConnectionCreated()
		{
			Interlocked.Increment(ref totalConnectionsCreated);
			var current = Interlocked.Increment(ref activeConnections);
			UpdatePeakConnections(current);
		}

		/// <summary>
		/// Records a connection being disposed.
		/// Thread-safe operation.
		/// </summary>
		public void RecordConnectionDisposed()
		{
			Interlocked.Increment(ref totalConnectionsDisposed);
			Interlocked.Decrement(ref activeConnections);
		}

		/// <summary>
		/// Records a connection error.
		/// Thread-safe operation.
		/// </summary>
		public void RecordConnectionError()
		{
			Interlocked.Increment(ref connectionErrors);
		}

		/// <summary>
		/// Records a pool exhaustion event.
		/// Thread-safe operation.
		/// </summary>
		public void RecordPoolExhaustion()
		{
			Interlocked.Increment(ref poolExhaustionCount);
		}

		/// <summary>
		/// Resets all metrics to zero.
		/// Use with caution - typically only for testing or maintenance windows.
		/// </summary>
		public void Reset()
		{
			lock (lockObject)
			{
				Interlocked.Exchange(ref totalConnectionsCreated, 0);
				Interlocked.Exchange(ref totalConnectionsDisposed, 0);
				Interlocked.Exchange(ref activeConnections, 0);
				Interlocked.Exchange(ref peakActiveConnections, 0);
				Interlocked.Exchange(ref connectionErrors, 0);
				Interlocked.Exchange(ref poolExhaustionCount, 0);
			}
		}

		/// <summary>
		/// Updates the peak connections counter if the current value is higher.
		/// Thread-safe operation using lock-free compare-exchange.
		/// </summary>
		/// <param name="currentActive">The current active connection count.</param>
		private void UpdatePeakConnections(long currentActive)
		{
			long currentPeak;
			do
			{
				currentPeak = Interlocked.Read(ref peakActiveConnections);
				if (currentActive <= currentPeak)
					break;
			}
			while (Interlocked.CompareExchange(ref peakActiveConnections, currentActive, currentPeak) != currentPeak);
		}

		/// <summary>
		/// Returns a formatted string representation of the current metrics.
		/// </summary>
		/// <returns>Formatted metrics summary.</returns>
		public override string ToString()
		{
			return $"Active: {ActiveConnections}, Peak: {PeakActiveConnections}, " +
				   $"Created: {TotalConnectionsCreated}, Disposed: {TotalConnectionsDisposed}, " +
				   $"Errors: {ConnectionErrors}, Exhausted: {PoolExhaustionCount}";
		}

		/// <summary>
		/// Assesses the health of the connection pool based on utilization and error metrics.
		/// </summary>
		/// <param name="maxPoolSize">The configured maximum pool size.</param>
		/// <param name="warningThreshold">Utilization percentage threshold for warning status (default: 70%).</param>
		/// <param name="criticalThreshold">Utilization percentage threshold for critical status (default: 85%).</param>
		/// <returns>A PoolHealthResult containing the health assessment and recommendations.</returns>
		/// <remarks>
		/// Health status determination logic:
		/// - Healthy: Utilization below warning threshold, no recent exhaustion events
		/// - Warning: Utilization between warning and critical thresholds
		/// - Critical: Utilization above critical threshold or frequent exhaustion events
		/// - Unhealthy: Active exhaustion events or high connection error rate
		/// </remarks>
		public PoolHealthResult GetPoolHealth(
			int maxPoolSize,
			double warningThreshold = 70.0,
			double criticalThreshold = 85.0)
		{
			var result = new PoolHealthResult
			{
				MaxPoolSize = maxPoolSize,
				ActiveConnections = ActiveConnections,
				PeakActiveConnections = PeakActiveConnections,
				PoolExhaustionCount = PoolExhaustionCount,
				ConnectionErrors = ConnectionErrors,
				UtilizationPercent = GetUtilizationPercentage(maxPoolSize)
			};

			// Cannot assess health if max pool size is invalid
			if (maxPoolSize <= 0 || result.UtilizationPercent < 0)
			{
				result.Status = PoolHealthStatus.Unknown;
				result.Message = "Pool health cannot be determined - invalid configuration";
				result.RecommendedAction = "Verify connection pool configuration";
				return result;
			}

			var utilization = result.UtilizationPercent;
			var exhaustionCount = result.PoolExhaustionCount;
			var errorCount = result.ConnectionErrors;
			var totalCreated = TotalConnectionsCreated;

			// Calculate error rate (errors per 100 connections)
			double errorRate = totalCreated > 0 ? (errorCount / (double)totalCreated) * 100.0 : 0;

			// Determine health status based on multiple factors
			if (exhaustionCount > 0 && utilization >= criticalThreshold)
			{
				// Active exhaustion with high utilization = Unhealthy
				result.Status = PoolHealthStatus.Unhealthy;
				result.Message = $"Pool exhausted {exhaustionCount} time(s) with {utilization:F1}% utilization";
				result.RequiresAction = true;
				result.RecommendedAction = "CRITICAL: Increase MaxPoolSize immediately or investigate connection leaks";
			}
			else if (errorRate > 10.0)
			{
				// High error rate = Unhealthy
				result.Status = PoolHealthStatus.Unhealthy;
				result.Message = $"High connection error rate: {errorRate:F1}% ({errorCount} errors)";
				result.RequiresAction = true;
				result.RecommendedAction = "Investigate database connectivity issues and connection string configuration";
			}
			else if (utilization >= criticalThreshold)
			{
				// Critical utilization threshold exceeded
				result.Status = PoolHealthStatus.Critical;
				result.Message = $"Pool utilization critical: {utilization:F1}% (threshold: {criticalThreshold}%)";
				result.RequiresAction = true;
				result.RecommendedAction = "Consider increasing MaxPoolSize or optimizing query execution time";
			}
			else if (exhaustionCount > 0 || errorRate > 5.0)
			{
				// Some exhaustion events or moderate error rate = Critical
				result.Status = PoolHealthStatus.Critical;
				result.Message = exhaustionCount > 0
					? $"Pool exhaustion detected: {exhaustionCount} event(s)"
					: $"Elevated error rate: {errorRate:F1}%";
				result.RequiresAction = true;
				result.RecommendedAction = "Monitor pool metrics closely and investigate connection usage patterns";
			}
			else if (utilization >= warningThreshold)
			{
				// Warning utilization threshold exceeded
				result.Status = PoolHealthStatus.Warning;
				result.Message = $"Pool utilization elevated: {utilization:F1}% (threshold: {warningThreshold}%)";
				result.RequiresAction = false;
				result.RecommendedAction = "Monitor pool utilization trends and consider scaling if sustained";
			}
			else
			{
				// All metrics within healthy ranges
				result.Status = PoolHealthStatus.Healthy;
				result.Message = $"Pool operating normally: {utilization:F1}% utilization";
				result.RequiresAction = false;
				result.RecommendedAction = "No action required";
			}

			return result;
		}
	}
}