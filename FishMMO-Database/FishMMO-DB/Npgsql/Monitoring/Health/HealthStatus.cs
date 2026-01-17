namespace FishMMO.Database.Npgsql.Monitoring.Health
{
	/// <summary>
	/// Represents the health status of a database connection.
	/// </summary>
	public enum HealthStatus
	{
		/// <summary>
		/// Health status has not been determined yet.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// Database is healthy and operating normally.
		/// </summary>
		Healthy = 1,

		/// <summary>
		/// Database is responding but performance is degraded.
		/// </summary>
		Degraded = 2,

		/// <summary>
		/// Database is unreachable or not functioning.
		/// </summary>
		Unhealthy = 3
	}
}