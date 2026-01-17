using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FishMMO.Database.Npgsql.Monitoring.Diagnostics
{
	/// <summary>
	/// Tracks query-level performance metrics for individual database operations.
	/// Thread-safe implementation with configurable tracking levels and overhead control.
	/// Complements DatabaseMetricsTracker with operation-specific insights.
	/// </summary>
	public sealed class QueryPerformanceTracker
	{
		private readonly QueryPerformanceConfiguration configuration;
		private readonly ConcurrentDictionary<string, QueryMetrics> operationMetrics;
		private readonly Random random;
		private volatile bool isEnabled;
		private volatile TrackingLevel currentLevel;

		/// <summary>
		/// Event raised when a slow query is detected.
		/// </summary>
		public event EventHandler<SlowQueryEventArgs> SlowQueryDetected;

		/// <summary>
		/// Gets or sets whether tracking is enabled.
		/// Can be toggled at runtime.
		/// </summary>
		public bool IsEnabled
		{
			get => isEnabled;
			set => isEnabled = value;
		}

		/// <summary>
		/// Gets or sets the current tracking level.
		/// Can be changed at runtime.
		/// </summary>
		public TrackingLevel Level
		{
			get => currentLevel;
			set => currentLevel = value;
		}

		/// <summary>
		/// Gets the configuration for this tracker.
		/// </summary>
		public QueryPerformanceConfiguration Configuration => configuration;

		/// <summary>
		/// Initializes a new instance of QueryPerformanceTracker.
		/// </summary>
		/// <param name="configuration">Configuration for tracking behavior. If null, uses default configuration.</param>
		public QueryPerformanceTracker(QueryPerformanceConfiguration configuration = null)
		{
			this.configuration = configuration ?? new QueryPerformanceConfiguration();
			this.operationMetrics = new ConcurrentDictionary<string, QueryMetrics>();
			this.random = new Random();
			this.isEnabled = this.configuration.Enabled;
			this.currentLevel = this.configuration.Level;
		}

		/// <summary>
		/// Records a query execution with automatic tracking level handling.
		/// Zero overhead when disabled or level is None.
		/// </summary>
		/// <param name="operationName">The name of the database operation (e.g., "CharacterService.GetByIdAsync").</param>
		/// <param name="duration">The execution duration.</param>
		/// <param name="success">Whether the execution succeeded.</param>
		public void RecordQuery(string operationName, TimeSpan duration, bool success)
		{
			if (!isEnabled || currentLevel == TrackingLevel.None)
				return;

			if (string.IsNullOrWhiteSpace(operationName))
				return;

			var durationMs = duration.TotalMilliseconds;

			// Always track slow queries regardless of level
			if (configuration.AlwaysLogSlowQueries && durationMs >= configuration.SlowQueryThresholdMs)
			{
				OnSlowQueryDetected(operationName, duration, success);
			}

			// Apply tracking based on level
			switch (currentLevel)
			{
				case TrackingLevel.Basic:
					RecordBasicMetrics(operationName, duration, success);
					break;

				case TrackingLevel.Standard:
					if (ShouldSample())
						RecordStandardMetrics(operationName, duration, success);
					break;

				case TrackingLevel.Detailed:
					if (ShouldSample())
						RecordDetailedMetrics(operationName, duration, success);
					break;

				case TrackingLevel.Full:
					RecordFullMetrics(operationName, duration, success);
					break;
			}
		}

		/// <summary>
		/// Starts a stopwatch for tracking query execution time.
		/// Returns null if tracking is disabled to avoid overhead.
		/// </summary>
		/// <returns>A started Stopwatch or null if tracking is disabled.</returns>
		public Stopwatch? StartTracking()
		{
			return isEnabled && currentLevel != TrackingLevel.None ? Stopwatch.StartNew() : null;
		}

		/// <summary>
		/// Gets metrics for a specific operation.
		/// </summary>
		/// <param name="operationName">The operation name.</param>
		/// <returns>The metrics or null if not found.</returns>
		public QueryMetrics? GetMetrics(string operationName)
		{
			return operationMetrics.TryGetValue(operationName, out var metrics) ? metrics : null;
		}

		/// <summary>
		/// Gets all tracked operation metrics.
		/// </summary>
		/// <returns>Collection of all operation metrics.</returns>
		public IReadOnlyCollection<QueryMetrics> GetAllMetrics()
		{
			return operationMetrics.Values.ToList();
		}

		/// <summary>
		/// Gets the N slowest operations by average execution time.
		/// </summary>
		/// <param name="count">Number of operations to return.</param>
		/// <returns>Collection of slowest operations.</returns>
		public IReadOnlyCollection<QueryMetrics> GetSlowestOperations(int count)
		{
			return operationMetrics.Values
				.OrderByDescending(m => m.AverageMs)
				.Take(count)
				.ToList();
		}

		/// <summary>
		/// Gets operations with the most slow query occurrences.
		/// </summary>
		/// <param name="count">Number of operations to return.</param>
		/// <returns>Collection of operations with most slow queries.</returns>
		public IReadOnlyCollection<QueryMetrics> GetMostSlowQueries(int count)
		{
			return operationMetrics.Values
				.Where(m => m.SlowQueryCount > 0)
				.OrderByDescending(m => m.SlowQueryCount)
				.Take(count)
				.ToList();
		}

		/// <summary>
		/// Resets all metrics.
		/// </summary>
		public void ResetAll()
		{
			foreach (var metric in operationMetrics.Values)
			{
				metric.Reset();
			}
		}

		/// <summary>
		/// Clears all tracked operations.
		/// </summary>
		public void Clear()
		{
			operationMetrics.Clear();
		}

		/// <summary>
		/// Records basic metrics (count and average time only).
		/// </summary>
		private void RecordBasicMetrics(string operationName, TimeSpan duration, bool success)
		{
			if (!configuration.TrackPerOperationMetrics)
				return;

			var metrics = GetOrCreateMetrics(operationName);
			if (metrics != null)
			{
				// For basic level, we still record all data but only expose count and average
				metrics.RecordExecution(duration, success, configuration.SlowQueryThresholdMs);
			}
		}

		/// <summary>
		/// Records standard metrics with sampling (includes percentiles).
		/// </summary>
		private void RecordStandardMetrics(string operationName, TimeSpan duration, bool success)
		{
			RecordBasicMetrics(operationName, duration, success);
		}

		/// <summary>
		/// Records detailed metrics with sampling.
		/// </summary>
		private void RecordDetailedMetrics(string operationName, TimeSpan duration, bool success)
		{
			RecordStandardMetrics(operationName, duration, success);
		}

		/// <summary>
		/// Records full metrics without sampling.
		/// </summary>
		private void RecordFullMetrics(string operationName, TimeSpan duration, bool success)
		{
			RecordBasicMetrics(operationName, duration, success);
		}

		/// <summary>
		/// Gets or creates metrics for an operation.
		/// Enforces maximum tracked operations limit.
		/// </summary>
		private QueryMetrics? GetOrCreateMetrics(string operationName)
		{
			if (operationMetrics.TryGetValue(operationName, out var metrics))
				return metrics;

			// Check limit
			if (operationMetrics.Count >= configuration.MaxTrackedOperations)
				return null;

			return operationMetrics.GetOrAdd(
				operationName,
				name => new QueryMetrics(name, configuration.MaxRecentSamplesPerOperation));
		}

		/// <summary>
		/// Determines if the current execution should be sampled based on configuration.
		/// </summary>
		private bool ShouldSample()
		{
			if (configuration.SampleRate >= 1.0)
				return true;

			return random.NextDouble() < configuration.SampleRate;
		}

		/// <summary>
		/// Raises the SlowQueryDetected event.
		/// </summary>
		private void OnSlowQueryDetected(string operationName, TimeSpan duration, bool success)
		{
			SlowQueryDetected?.Invoke(this, new SlowQueryEventArgs(
				operationName,
				duration,
				Configuration.SlowQueryThresholdMs,
				success,
				DateTime.UtcNow));
		}
	}

	/// <summary>
	/// Event arguments for slow query detection.
	/// </summary>
	public sealed class SlowQueryEventArgs : EventArgs
	{
		/// <summary>
		/// Gets the operation name.
		/// </summary>
		public string OperationName { get; }

		/// <summary>
		/// Gets the execution duration.
		/// </summary>
		public TimeSpan Duration { get; }

		/// <summary>
		/// Gets the execution duration in milliseconds.
		/// </summary>
		public double DurationMs { get; }

		/// <summary>
		/// Gets the slow query threshold in milliseconds.
		/// </summary>
		public double ThresholdMs { get; }

		/// <summary>
		/// Gets whether the query succeeded.
		/// </summary>
		public bool Success { get; }

		/// <summary>
		/// Gets the timestamp when the slow query was detected.
		/// </summary>
		public DateTime Timestamp { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="SlowQueryEventArgs"/> class.
		/// </summary>
		public SlowQueryEventArgs(string operationName, TimeSpan duration, double thresholdMs, bool success, DateTime timestamp)
		{
			OperationName = operationName;
			Duration = duration;
			DurationMs = duration.TotalMilliseconds;
			ThresholdMs = thresholdMs;
			Success = success;
			Timestamp = timestamp;
		}
	}
}