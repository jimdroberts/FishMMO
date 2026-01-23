using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FishMMO.Database.Npgsql.Monitoring.Diagnostics
{
	/// <summary>
	/// Tracks performance metrics for a specific database operation.
	/// Thread-safe implementation using lock-free operations where possible.
	/// </summary>
	public sealed class QueryMetrics
	{
		private readonly string operationName;
		private readonly object lockObject = new object();
		private long totalExecutions;
		private long successfulExecutions;
		private long failedExecutions;
		private long totalDurationTicks;
		private long minDurationTicks = long.MaxValue;
		private long maxDurationTicks;
		private long slowQueryCount;
		private readonly Queue<double> recentExecutionTimesMs;
		private readonly int maxRecentSamples;

		/// <summary>
		/// Gets the name of the database operation being tracked.
		/// </summary>
		public string OperationName => operationName;

		/// <summary>
		/// Gets the total number of executions.
		/// </summary>
		public long TotalExecutions => Interlocked.Read(ref totalExecutions);

		/// <summary>
		/// Gets the number of successful executions.
		/// </summary>
		public long SuccessfulExecutions => Interlocked.Read(ref successfulExecutions);

		/// <summary>
		/// Gets the number of failed executions.
		/// </summary>
		public long FailedExecutions => Interlocked.Read(ref failedExecutions);

		/// <summary>
		/// Gets the success rate as a percentage (0-100).
		/// </summary>
		public double SuccessRate
		{
			get
			{
				var total = Interlocked.Read(ref totalExecutions);
				if (total == 0) return 0;
				var successful = Interlocked.Read(ref successfulExecutions);
				return (double)successful / total * 100.0;
			}
		}

		/// <summary>
		/// Gets the average execution time in milliseconds.
		/// </summary>
		public double AverageMs
		{
			get
			{
				var total = Interlocked.Read(ref totalExecutions);
				if (total == 0) return 0;
				var ticks = Interlocked.Read(ref totalDurationTicks);
				return TimeSpan.FromTicks(ticks / total).TotalMilliseconds;
			}
		}

		/// <summary>
		/// Gets the minimum execution time in milliseconds.
		/// </summary>
		public double MinMs
		{
			get
			{
				var ticks = Interlocked.Read(ref minDurationTicks);
				return ticks == long.MaxValue ? 0 : TimeSpan.FromTicks(ticks).TotalMilliseconds;
			}
		}

		/// <summary>
		/// Gets the maximum execution time in milliseconds.
		/// </summary>
		public double MaxMs
		{
			get
			{
				var ticks = Interlocked.Read(ref maxDurationTicks);
				return TimeSpan.FromTicks(ticks).TotalMilliseconds;
			}
		}

		/// <summary>
		/// Gets the number of executions that exceeded the slow query threshold.
		/// </summary>
		public long SlowQueryCount => Interlocked.Read(ref slowQueryCount);

		/// <summary>
		/// Gets the 95th percentile execution time in milliseconds.
		/// Returns 0 if insufficient samples.
		/// </summary>
		public double P95Ms
		{
			get
			{
				return CalculatePercentileSnapshot(0.95);
			}
		}

		/// <summary>
		/// Gets the 99th percentile execution time in milliseconds.
		/// Returns 0 if insufficient samples.
		/// </summary>
		public double P99Ms
		{
			get
			{
				return CalculatePercentileSnapshot(0.99);
			}
		}

		/// <summary>
		/// Initializes a new instance of QueryMetrics.
		/// </summary>
		/// <param name="operationName">The name of the database operation.</param>
		/// <param name="maxRecentSamples">Maximum number of recent samples to keep for percentile calculation.</param>
		public QueryMetrics(string operationName, int maxRecentSamples = 1000)
		{
			this.operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
			this.maxRecentSamples = maxRecentSamples;
			this.recentExecutionTimesMs = new Queue<double>(maxRecentSamples);
		}

		/// <summary>
		/// Records a query execution.
		/// Thread-safe operation.
		/// </summary>
		/// <param name="duration">The execution duration.</param>
		/// <param name="success">Whether the execution succeeded.</param>
		/// <param name="slowQueryThresholdMs">The threshold for considering a query slow.</param>
		public void RecordExecution(TimeSpan duration, bool success, double slowQueryThresholdMs = 1000)
		{
			Interlocked.Increment(ref totalExecutions);

			if (success)
				Interlocked.Increment(ref successfulExecutions);
			else
				Interlocked.Increment(ref failedExecutions);

			var ticks = duration.Ticks;
			Interlocked.Add(ref totalDurationTicks, ticks);
			UpdateMinMax(ticks);

			var durationMs = duration.TotalMilliseconds;
			if (durationMs >= slowQueryThresholdMs)
			{
				Interlocked.Increment(ref slowQueryCount);
			}

			// Add to recent samples for percentile calculation - O(1) operation
			lock (lockObject)
			{
				if (recentExecutionTimesMs.Count >= maxRecentSamples)
				{
					// Remove oldest sample (FIFO) - O(1) operation
					recentExecutionTimesMs.Dequeue();
				}
				recentExecutionTimesMs.Enqueue(durationMs);
			}
		}

		/// <summary>
		/// Resets all metrics to initial state.
		/// </summary>
		public void Reset()
		{
			lock (lockObject)
			{
				Interlocked.Exchange(ref totalExecutions, 0);
				Interlocked.Exchange(ref successfulExecutions, 0);
				Interlocked.Exchange(ref failedExecutions, 0);
				Interlocked.Exchange(ref totalDurationTicks, 0);
				Interlocked.Exchange(ref minDurationTicks, long.MaxValue);
				Interlocked.Exchange(ref maxDurationTicks, 0);
				Interlocked.Exchange(ref slowQueryCount, 0);
				recentExecutionTimesMs.Clear();
			}
		}

		/// <summary>
		/// Updates the minimum and maximum duration values.
		/// Thread-safe operation using compare-exchange.
		/// </summary>
		private void UpdateMinMax(long durationTicks)
		{
			// Update minimum
			long currentMin;
			do
			{
				currentMin = Interlocked.Read(ref minDurationTicks);
				if (durationTicks >= currentMin)
					break;
			}
			while (Interlocked.CompareExchange(ref minDurationTicks, durationTicks, currentMin) != currentMin);

			// Update maximum
			long currentMax;
			do
			{
				currentMax = Interlocked.Read(ref maxDurationTicks);
				if (durationTicks <= currentMax)
					break;
			}
			while (Interlocked.CompareExchange(ref maxDurationTicks, durationTicks, currentMax) != currentMax);
		}

		/// <summary>
		/// Calculates the specified percentile from recent execution times.
		/// Must be called within a lock.
		/// Sorts on demand - simpler and thread-safe at cost of O(n log n) per access.
		/// Performance impact is negligible for sample sizes up to 1000 elements.
		/// </summary>
		private double CalculatePercentileSnapshot(double percentile)
		{
			double[] snapshot;
			lock (lockObject)
			{
				if (recentExecutionTimesMs.Count == 0)
				{
					return 0;
				}

				snapshot = recentExecutionTimesMs.ToArray();
			}

			Array.Sort(snapshot);
			var index = (int)Math.Ceiling(percentile * snapshot.Length) - 1;
			if (index < 0) index = 0;
			if (index >= snapshot.Length) index = snapshot.Length - 1;
			return snapshot[index];
		}

		/// <summary>
		/// Returns a formatted string representation of the metrics.
		/// </summary>
		public override string ToString()
		{
			return $"{OperationName}: Avg={AverageMs:F2}ms, Min={MinMs:F2}ms, Max={MaxMs:F2}ms, " +
				   $"P95={P95Ms:F2}ms, P99={P99Ms:F2}ms, Executions={TotalExecutions}, " +
				   $"SuccessRate={SuccessRate:F1}%, SlowQueries={SlowQueryCount}";
		}
	}
}