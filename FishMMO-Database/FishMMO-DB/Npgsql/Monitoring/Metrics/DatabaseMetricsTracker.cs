using System;
using System.Collections.Generic;
using System.Threading;

namespace FishMMO.Database.Npgsql.Monitoring.Metrics
{
	/// <summary>
	/// Tracks database operation metrics in a thread-safe manner.
	/// Records query execution statistics including success/failure rates and response times.
	/// Follows Single Responsibility Principle: solely responsible for metrics collection and aggregation.
	/// Unity-compatible implementation with no external dependencies.
	/// </summary>
	public sealed class DatabaseMetricsTracker
	{
		private long totalQueries;
		private long successfulQueries;
		private long failedQueries;
		private long totalResponseTimeTicks;
		private long minResponseTimeTicks = long.MaxValue;
		private long maxResponseTimeTicks;
		private readonly object lockObject = new object();
		private readonly Dictionary<string, long> errorCountsByType;

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseMetricsTracker"/> class.
		/// </summary>
		public DatabaseMetricsTracker()
		{
			errorCountsByType = new Dictionary<string, long>();
		}

		/// <summary>
		/// Records a successful database operation.
		/// Thread-safe operation using atomic interlocked operations.
		/// </summary>
		/// <param name="responseTime">Time taken for the operation to complete.</param>
		public void RecordSuccess(TimeSpan responseTime)
		{
			Interlocked.Increment(ref totalQueries);
			Interlocked.Increment(ref successfulQueries);
			Interlocked.Add(ref totalResponseTimeTicks, responseTime.Ticks);
			UpdateMinMax(responseTime.Ticks);
		}

		/// <summary>
		/// Records a failed database operation.
		/// Thread-safe operation using atomic interlocked operations and locking for dictionary access.
		/// </summary>
		/// <param name="responseTime">Time taken before the operation failed.</param>
		/// <param name="errorType">Type or category of error that occurred (e.g., "Timeout", "ConnectionFailed").</param>
		public void RecordFailure(TimeSpan responseTime, string errorType)
		{
			if (string.IsNullOrWhiteSpace(errorType))
			{
				errorType = "Unknown";
			}

			var ticks = responseTime.Ticks;

			// NOTE: Interlocked inside lock is redundant but harmless. The lock already provides mutual exclusion.
			// RecordSuccess uses Interlocked WITHOUT a lock — the inconsistency is intentional
			// (lock needed here for errorCountsByType dictionary access).
			// Update all metrics atomically within lock to ensure consistency
			lock (lockObject)
			{
				Interlocked.Increment(ref totalQueries);
				Interlocked.Increment(ref failedQueries);
				Interlocked.Add(ref totalResponseTimeTicks, ticks);
				UpdateMinMax(ticks);

				if (!errorCountsByType.ContainsKey(errorType))
				{
					errorCountsByType[errorType] = 0;
				}
				errorCountsByType[errorType]++;
			}
		}

		/// <summary>
		/// Gets a snapshot of current metrics.
		/// Returns a new MetricsSummary instance with current values.
		/// Thread-safe operation.
		/// </summary>
		/// <returns>A MetricsSummary containing current metric values.</returns>
		public MetricsSummary GetSummary()
		{
			var total = Interlocked.Read(ref totalQueries);
			var successful = Interlocked.Read(ref successfulQueries);
			var failed = Interlocked.Read(ref failedQueries);
			var totalTicks = Interlocked.Read(ref totalResponseTimeTicks);
			var minTicks = Interlocked.Read(ref minResponseTimeTicks);
			var maxTicks = Interlocked.Read(ref maxResponseTimeTicks);

			var summary = new MetricsSummary
			{
				TotalQueries = total,
				SuccessfulQueries = successful,
				FailedQueries = failed,
				SuccessRate = CalculateSuccessRate(total, successful),
				AverageResponseTimeMs = CalculateAverageResponseTime(total, totalTicks),
				MinResponseTimeMs = minTicks != long.MaxValue ? TimeSpan.FromTicks(minTicks).TotalMilliseconds : 0,
				MaxResponseTimeMs = TimeSpan.FromTicks(maxTicks).TotalMilliseconds
			};

			lock (lockObject)
			{
				summary.ErrorCounts = new Dictionary<string, long>(errorCountsByType);
			}

			return summary;
		}

		/// <summary>
		/// Resets all metrics counters to their initial state.
		/// Thread-safe operation.
		/// </summary>
		public void Reset()
		{
			Interlocked.Exchange(ref totalQueries, 0);
			Interlocked.Exchange(ref successfulQueries, 0);
			Interlocked.Exchange(ref failedQueries, 0);
			Interlocked.Exchange(ref totalResponseTimeTicks, 0);
			Interlocked.Exchange(ref minResponseTimeTicks, long.MaxValue);
			Interlocked.Exchange(ref maxResponseTimeTicks, 0);

			lock (lockObject)
			{
				errorCountsByType.Clear();
			}
		}

		/// <summary>
		/// Updates minimum and maximum response time values atomically.
		/// Uses compare-and-swap pattern for lock-free updates.
		/// </summary>
		/// <param name="responseTicks">The response time in ticks to compare against current min/max.</param>
		private void UpdateMinMax(long responseTicks)
		{
			// Update min atomically using compare-and-swap
			long currentMin;
			do
			{
				currentMin = Interlocked.Read(ref minResponseTimeTicks);
				if (responseTicks >= currentMin) break;
			} while (Interlocked.CompareExchange(ref minResponseTimeTicks, responseTicks, currentMin) != currentMin);

			// Update max atomically using compare-and-swap
			long currentMax;
			do
			{
				currentMax = Interlocked.Read(ref maxResponseTimeTicks);
				if (responseTicks <= currentMax) break;
			} while (Interlocked.CompareExchange(ref maxResponseTimeTicks, responseTicks, currentMax) != currentMax);
		}

		/// <summary>
		/// Calculates the success rate as a percentage.
		/// </summary>
		/// <param name="total">Total number of queries.</param>
		/// <param name="successful">Number of successful queries.</param>
		/// <returns>Success rate as a percentage (0-100).</returns>
		private double CalculateSuccessRate(long total, long successful)
		{
			if (total == 0) return 100.0;
			return (successful / (double)total) * 100.0;
		}

		/// <summary>
		/// Calculates the average response time in milliseconds.
		/// </summary>
		/// <param name="total">Total number of queries.</param>
		/// <param name="totalTicks">Total accumulated response time in ticks.</param>
		/// <returns>Average response time in milliseconds.</returns>
		private double CalculateAverageResponseTime(long total, long totalTicks)
		{
			if (total == 0) return 0;
			return TimeSpan.FromTicks(totalTicks / total).TotalMilliseconds;
		}
	}
}