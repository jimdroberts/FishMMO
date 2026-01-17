using System;

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
		public NpgsqlSettings Npgsql { get; set; }

		/// <summary>
		/// Gets or sets Redis connection settings.
		/// </summary>
		public RedisSettings Redis { get; set; }
	}

	/// <summary>
	/// PostgreSQL connection settings.
	/// </summary>
	[Serializable]
	public class NpgsqlSettings
	{
		/// <summary>
		/// Gets or sets the database name.
		/// </summary>
		public string Database { get; set; }

		/// <summary>
		/// Gets or sets the database username.
		/// </summary>
		public string Username { get; set; }

		/// <summary>
		/// Gets or sets the database password.
		/// </summary>
		public string Password { get; set; }

		/// <summary>
		/// Gets or sets the database host.
		/// </summary>
		public string Host { get; set; }

		/// <summary>
		/// Gets or sets the database port.
		/// </summary>
		public string Port { get; set; }

		/// <summary>
		/// Gets or sets the command timeout in seconds (default: 10).
		/// Commands exceeding this timeout will be cancelled.
		/// </summary>
		public int CommandTimeout { get; set; } = 10;

		/// <summary>
		/// Gets or sets the connection timeout in seconds (default: 15).
		/// Time to wait while establishing a connection before terminating the attempt.
		/// Should typically be higher than CommandTimeout to allow for connection establishment.
		/// </summary>
		public int ConnectionTimeout { get; set; } = 15;

		/// <summary>
		/// Gets or sets the minimum connection pool size (default: 5).
		/// Keeps connections warm for better performance.
		/// </summary>
		public int MinPoolSize { get; set; } = 5;

		/// <summary>
		/// Gets or sets the maximum connection pool size (default: 100).
		/// Limits maximum concurrent connections to the database.
		/// </summary>
		public int MaxPoolSize { get; set; } = 100;

		/// <summary>
		/// Gets or sets the maximum retry count for transient database failures (default: 3).
		/// Number of times to retry a failed database operation before giving up.
		/// </summary>
		public int MaxRetryCount { get; set; } = 3;

		/// <summary>
		/// Gets or sets the maximum retry delay in seconds (default: 5).
		/// Maximum time to wait between retry attempts for transient failures.
		/// Uses exponential backoff: delay = min(2^attempt, MaxRetryDelay).
		/// </summary>
		public int MaxRetryDelaySeconds { get; set; } = 5;
	}

	/// <summary>
	/// Redis connection settings.
	/// </summary>
	[Serializable]
	public class RedisSettings
	{
		/// <summary>
		/// Gets or sets the Redis host.
		/// </summary>
		public string Host { get; set; }

		/// <summary>
		/// Gets or sets the Redis port.
		/// </summary>
		public string Port { get; set; }

		/// <summary>
		/// Gets or sets the Redis password.
		/// </summary>
		public string Password { get; set; }
	}
}