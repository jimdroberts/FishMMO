namespace FishMMO.Database.Npgsql.Monitoring.Health
{
	/// <summary>
	/// Represents the health status of the database connection pool.
	/// </summary>
	public enum PoolHealthStatus
	{
		/// <summary>
		/// Pool health status has not been determined yet.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// Pool is operating normally with healthy utilization levels.
		/// </summary>
		Healthy = 1,

		/// <summary>
		/// Pool utilization is approaching capacity but still functional.
		/// Warning level - may need attention.
		/// </summary>
		Warning = 2,

		/// <summary>
		/// Pool is at or near capacity with high risk of exhaustion.
		/// Critical level - immediate attention required.
		/// </summary>
		Critical = 3,

		/// <summary>
		/// Pool has been exhausted or has experienced repeated failures.
		/// Unhealthy state - service degradation likely.
		/// </summary>
		Unhealthy = 4
	}
}
