namespace FishMMO.Database.Npgsql.Monitoring.Diagnostics
{
	/// <summary>
	/// Configuration for query performance tracking.
	/// Can be loaded from appsettings.json or set programmatically.
	/// </summary>
	public sealed class QueryPerformanceConfiguration
	{
		/// <summary>
		/// Gets or sets whether query performance tracking is enabled.
		/// Default: true (Basic level).
		/// </summary>
		public bool Enabled { get; set; } = true;

		/// <summary>
		/// Gets or sets the tracking detail level.
		/// Default: Basic.
		/// </summary>
		public TrackingLevel Level { get; set; } = TrackingLevel.Basic;

		/// <summary>
		/// Gets or sets the threshold in milliseconds for considering a query slow.
		/// Queries exceeding this threshold are always logged regardless of tracking level.
		/// Default: 1000ms (1 second).
		/// </summary>
		public double SlowQueryThresholdMs { get; set; } = 1000;

		/// <summary>
		/// Gets or sets whether slow queries should always be logged.
		/// Default: true.
		/// </summary>
		public bool AlwaysLogSlowQueries { get; set; } = true;

		/// <summary>
		/// Gets or sets the sample rate for detailed tracking (0.0 to 1.0).
		/// 1.0 = 100% sampling, 0.1 = 10% sampling.
		/// Only applies to Standard and Detailed levels.
		/// Default: 0.1 (10%).
		/// </summary>
		public double SampleRate { get; set; } = 0.1;

		/// <summary>
		/// Gets or sets the maximum number of recent samples to keep per operation for percentile calculations.
		/// Default: 1000.
		/// </summary>
		public int MaxRecentSamplesPerOperation { get; set; } = 1000;

		/// <summary>
		/// Gets or sets whether to track per-operation metrics.
		/// Default: true.
		/// </summary>
		public bool TrackPerOperationMetrics { get; set; } = true;

		/// <summary>
		/// Gets or sets the maximum number of unique operations to track.
		/// Prevents memory exhaustion if operation names are dynamically generated.
		/// Default: 1000.
		/// </summary>
		public int MaxTrackedOperations { get; set; } = 1000;
	}
}