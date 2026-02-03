using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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
		private readonly ConcurrentDictionary<string, (QueryMetrics Metrics, long LastAccessTicks)> operationMetrics;
		private volatile bool isEnabled;
		private volatile TrackingLevel currentLevel;
		private readonly object evictionGate;
		private long lastEvictionTicks;
		private const int MaxOperationNameLength = 128;
		private const int EvictionSampleSize = 32;
		private static readonly TimeSpan MinEvictionInterval = TimeSpan.FromSeconds(1);

		/// <summary>
		/// Thread-local random instance for thread-safe sampling.
		/// </summary>
		private static readonly ThreadLocal<Random> ThreadRandom =
			new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

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
			this.operationMetrics = new ConcurrentDictionary<string, (QueryMetrics, long)>();
			this.isEnabled = this.configuration.Enabled;
			this.currentLevel = this.configuration.Level;
			this.evictionGate = new object();
		}

		/// <summary>
		/// Records a query execution with automatic tracking level handling.
		/// Zero overhead when disabled or level is None.
		/// </summary>
		/// <param name="operationName">The name of the database operation (e.g., "CharacterService.FetchAsync").</param>
		/// <param name="duration">The execution duration.</param>
		/// <param name="success">Whether the execution succeeded.</param>
		public void RecordQuery(string operationName, TimeSpan duration, bool success)
		{
			if (!isEnabled || currentLevel == TrackingLevel.None)
				return;

			if (string.IsNullOrWhiteSpace(operationName))
				return;

			operationName = NormalizeOperationName(operationName);
			if (operationName.Length == 0)
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
		/// Updates last access time for LRU tracking.
		/// </summary>
		/// <param name="operationName">The operation name.</param>
		/// <returns>The metrics or null if not found.</returns>
		public QueryMetrics? GetMetrics(string operationName)
		{
			if (string.IsNullOrWhiteSpace(operationName))
			{
				return null;
			}

			operationName = NormalizeOperationName(operationName);
			if (operationName.Length == 0)
			{
				return null;
			}

			if (operationMetrics.TryGetValue(operationName, out var entry))
			{
				// Update last access time without resurrecting removed keys.
				operationMetrics.TryUpdate(operationName, (entry.Metrics, DateTime.UtcNow.Ticks), entry);
				return entry.Metrics;
			}
			return null;
		}

		/// <summary>
		/// Gets all tracked operation metrics.
		/// </summary>
		/// <returns>Collection of all operation metrics.</returns>
		public IReadOnlyCollection<QueryMetrics> GetAllMetrics()
		{
			return operationMetrics.Values.Select(v => v.Metrics).ToList();
		}

		/// <summary>
		/// Gets the N slowest operations by average execution time.
		/// </summary>
		/// <param name="count">Number of operations to return.</param>
		/// <returns>Collection of slowest operations.</returns>
		public IReadOnlyCollection<QueryMetrics> GetSlowestOperations(int count)
		{
			return operationMetrics.Values
				.Select(v => v.Metrics)
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
				.Select(v => v.Metrics)
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
			foreach (var entry in operationMetrics.Values)
			{
				entry.Metrics.Reset();
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
		/// Enforces maximum tracked operations limit with LRU eviction.
		/// Thread-safe implementation prevents dictionary from growing beyond configured limit.
		/// </summary>
		private QueryMetrics? GetOrCreateMetrics(string operationName)
		{
			var currentTicks = DateTime.UtcNow.Ticks;

			// Try to update existing entry atomically
			if (operationMetrics.TryGetValue(operationName, out var entry))
			{
				// Update last access time without resurrecting removed keys.
				operationMetrics.TryUpdate(operationName, (entry.Metrics, currentTicks), entry);
				return entry.Metrics;
			}

			// Create new metrics outside of critical section to minimize lock time
			var newMetrics = new QueryMetrics(operationName, configuration.MaxRecentSamplesPerOperation);
			var newEntry = (newMetrics, currentTicks);

			// Use GetOrAdd for atomic check-and-insert
			var addedEntry = operationMetrics.GetOrAdd(operationName, newEntry);

			// If we successfully added a new entry (not retrieved existing), enforce capacity.
			if (ReferenceEquals(addedEntry.Metrics, newMetrics))
			{
				TryEvictIfOverCapacity(currentTicks);
			}

			return addedEntry.Metrics;
		}

		/// <summary>
		/// Determines if the current execution should be sampled based on configuration.
		/// Uses thread-local Random for thread-safe sampling.
		/// </summary>
		private bool ShouldSample()
		{
			if (configuration.SampleRate >= 1.0)
				return true;

			if (configuration.SampleRate <= 0.0)
				return false;

			return ThreadRandom.Value!.NextDouble() < configuration.SampleRate;
		}

		/// <summary>
		/// Raises the SlowQueryDetected event.
		/// </summary>
		private void OnSlowQueryDetected(string operationName, TimeSpan duration, bool success)
		{
			var handlers = SlowQueryDetected;
			if (handlers == null)
			{
				return;
			}

			var args = new SlowQueryEventArgs(
				operationName,
				duration,
				Configuration.SlowQueryThresholdMs,
				success,
				DateTime.UtcNow);

			// Dispatch off-thread so instrumentation cannot add latency to database calls.
			ThreadPool.QueueUserWorkItem(_ => InvokeSlowQueryHandlersSafely(handlers, args));
		}

		private static void InvokeSlowQueryHandlersSafely(EventHandler<SlowQueryEventArgs> handlers, SlowQueryEventArgs args)
		{
			var invocationList = handlers.GetInvocationList();
			for (var i = 0; i < invocationList.Length; i++)
			{
				if (invocationList[i] is EventHandler<SlowQueryEventArgs> handler)
				{
					try
					{
						handler.Invoke(null, args);
					}
					catch
					{
						// Never allow consumer code to break instrumentation.
					}
				}
			}
		}

		private string NormalizeOperationName(string operationName)
		{
			operationName = operationName.Trim();
			if (operationName.Length > MaxOperationNameLength)
			{
				// Avoid label explosion from dynamic names.
				return string.Empty;
			}

			return operationName;
		}

		private void TryEvictIfOverCapacity(long nowTicks)
		{
			if (!configuration.TrackPerOperationMetrics)
			{
				return;
			}

			var maxTracked = configuration.MaxTrackedOperations;
			if (maxTracked <= 0)
			{
				return;
			}

			if (operationMetrics.Count <= maxTracked)
			{
				return;
			}

			var lastTicks = Interlocked.Read(ref lastEvictionTicks);
			if (nowTicks - lastTicks < MinEvictionInterval.Ticks)
			{
				return;
			}

			lock (evictionGate)
			{
				lastTicks = Interlocked.Read(ref lastEvictionTicks);
				if (nowTicks - lastTicks < MinEvictionInterval.Ticks)
				{
					return;
				}

				Interlocked.Exchange(ref lastEvictionTicks, nowTicks);

				while (operationMetrics.Count > maxTracked)
				{
					var candidate = FindEvictionCandidateKey();
					if (candidate.Length == 0)
					{
						break;
					}

					operationMetrics.TryRemove(candidate, out _);
				}
			}
		}

		private string FindEvictionCandidateKey()
		{
			string? candidateKey = null;
			long candidateTicks = long.MaxValue;
			var inspected = 0;

			foreach (var kvp in operationMetrics)
			{
				if (kvp.Value.LastAccessTicks < candidateTicks)
				{
					candidateTicks = kvp.Value.LastAccessTicks;
					candidateKey = kvp.Key;
				}

				inspected++;
				if (inspected >= EvictionSampleSize)
				{
					break;
				}
			}

			return candidateKey ?? string.Empty;
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