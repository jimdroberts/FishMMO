using System;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;

namespace FishMMO.Database
{
	/// <summary>
	/// Application settings for database connections.
	/// </summary>
	[Serializable]
	public class AppSettings
	{
		/// <summary>
		/// Gets or sets PostgreSQL connection settings.
		/// </summary>
		public NpgsqlSettings Npgsql { get; set; } = new();
	}

	/// <summary>
	/// PostgreSQL connection settings.
	/// </summary>
	[Serializable]
	public class NpgsqlSettings
	{
		/// <summary>
		/// Gets or sets the PostgreSQL host.
		/// </summary>
		public string Host { get; set; } = "127.0.0.1";

		/// <summary>
		/// Gets or sets the PostgreSQL port.
		/// </summary>
		public string Port { get; set; } = "5432";

		/// <summary>
		/// Gets or sets the PostgreSQL database name.
		/// </summary>
		public string Database { get; set; } = "fishmmo";

		/// <summary>
		/// Gets or sets the database schema name.
		/// </summary>
		public string Schema { get; set; } = "public";

		/// <summary>
		/// Gets or sets the command timeout in seconds.
		/// </summary>
		public int CommandTimeout { get; set; } = 10;

		/// <summary>
		/// Gets or sets the connection timeout in seconds.
		/// </summary>
		public int ConnectionTimeout { get; set; } = 15;

		/// <summary>
		/// Gets or sets the minimum connection pool size.
		/// </summary>
		public int MinPoolSize { get; set; } = 5;

		/// <summary>
		/// Gets or sets the maximum connection pool size.
		/// </summary>
		public int MaxPoolSize { get; set; } = 100;

		/// <summary>
		/// Gets or sets query performance tracking configuration.
		/// </summary>
		public QueryPerformanceConfiguration QueryPerformanceTracking { get; set; } = new();

		/// <summary>
		/// Gets or sets the retry policy configuration for transient failure handling.
		/// </summary>
		public RetryPolicyConfiguration RetryPolicy { get; set; } = new();
	}

	/// <summary>
	/// Configuration for tracking and logging slow database queries.
	/// </summary>
	[Serializable]
	public class QueryPerformanceConfiguration
	{
		/// <summary>
		/// Gets or sets whether query performance tracking is enabled.
		/// </summary>
		public bool Enabled { get; set; } = false;

		/// <summary>
		/// Gets or sets the tracking verbosity level.
		/// </summary>
		public TrackingLevel Level { get; set; } = TrackingLevel.Basic;

		/// <summary>
		/// Gets or sets the threshold in milliseconds for classifying queries as slow.
		/// </summary>
		public double SlowQueryThresholdMs { get; set; } = 100.0;

		/// <summary>
		/// Gets or sets sampling rate for tracked queries in range 0..1.
		/// </summary>
		public double SampleRate { get; set; } = 1.0;
	}

	/// <summary>
	/// Configuration for handling database connection retries.
	/// </summary>
	[Serializable]
	public class RetryPolicyConfiguration
	{
		/// <summary>
		/// Gets or sets the maximum retry attempts for transient failures.
		/// </summary>
		public int MaxRetries { get; set; } = 3;

		/// <summary>
		/// Gets or sets the base delay in milliseconds between retries.
		/// </summary>
		public int BaseDelayMs { get; set; } = 1000;

		/// <summary>
		/// Gets or sets the maximum random jitter in milliseconds added to retry delays.
		/// </summary>
		public int MaxJitterMs { get; set; } = 100;
	}

}