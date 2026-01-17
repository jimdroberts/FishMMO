namespace FishMMO.Database.Npgsql.Monitoring.Health
{
	/// <summary>
	/// Contains the result of a connection pool health assessment.
	/// Provides detailed information about pool utilization, exhaustion events, and recommended actions.
	/// </summary>
	public sealed class PoolHealthResult
	{
		/// <summary>
		/// Gets or sets the overall health status of the connection pool.
		/// </summary>
		public PoolHealthStatus Status { get; set; }

		/// <summary>
		/// Gets or sets a human-readable message describing the pool health status.
		/// </summary>
		public string Message { get; set; }

		/// <summary>
		/// Gets or sets the current pool utilization percentage (0-100).
		/// </summary>
		public double UtilizationPercent { get; set; }

		/// <summary>
		/// Gets or sets the number of active connections.
		/// </summary>
		public long ActiveConnections { get; set; }

		/// <summary>
		/// Gets or sets the maximum pool size.
		/// </summary>
		public int MaxPoolSize { get; set; }

		/// <summary>
		/// Gets or sets the peak active connections count.
		/// </summary>
		public long PeakActiveConnections { get; set; }

		/// <summary>
		/// Gets or sets the total number of pool exhaustion events.
		/// </summary>
		public long PoolExhaustionCount { get; set; }

		/// <summary>
		/// Gets or sets the total number of connection errors.
		/// </summary>
		public long ConnectionErrors { get; set; }

		/// <summary>
		/// Gets or sets whether immediate action is recommended.
		/// </summary>
		public bool RequiresAction { get; set; }

		/// <summary>
		/// Gets or sets recommended action to take based on the pool health status.
		/// </summary>
		public string RecommendedAction { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="PoolHealthResult"/> class with default values.
		/// </summary>
		public PoolHealthResult()
		{
			Status = PoolHealthStatus.Unknown;
			Message = string.Empty;
			UtilizationPercent = 0;
			ActiveConnections = 0;
			MaxPoolSize = 0;
			PeakActiveConnections = 0;
			PoolExhaustionCount = 0;
			ConnectionErrors = 0;
			RequiresAction = false;
			RecommendedAction = string.Empty;
		}

		/// <summary>
		/// Returns a string representation of the pool health result.
		/// </summary>
		/// <returns>A formatted string containing the key pool health information.</returns>
		public override string ToString()
		{
			return $"[{Status}] {Message} | Utilization: {UtilizationPercent:F1}%, Active: {ActiveConnections}/{MaxPoolSize}";
		}
	}
}
